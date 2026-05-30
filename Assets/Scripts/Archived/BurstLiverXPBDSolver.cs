using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using System.Collections.Generic;

public class BurstLiverXPBDSolver : MonoBehaviour
{
    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f, 0);
    public float drag = 0.4f;
    public float surfaceCompliance = 0.0005f;
    public float volumeCompliance = 0.005f; 
    public int substeps = 10;
    public int iterations = 2;

    [Header("Collision & Anchors")]
    public float floorLevel = 0.0f;
    public bool anchorTopVertices = true;

    // High-Performance Native Arrays visible to the binding script
    public NativeArray<Vector3> positions;
    private NativeArray<Vector3> predictedPositions;
    private NativeArray<Vector3> velocities;
    private NativeArray<float> invMasses;

    private NativeArray<int> constraintIndicesA;
    private NativeArray<int> constraintIndicesB;
    private NativeArray<float> constraintRestLengths;
    private NativeArray<float> constraintLambdas;
    private NativeArray<float> constraintCompliances;

    private MeshFilter meshFilter;
    private Mesh simulationMesh;
    private Vector3[] dynamicLocalVertices;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        simulationMesh = Instantiate(meshFilter.sharedMesh);
        meshFilter.mesh = simulationMesh;

        Vector3[] initialLocalVertices = simulationMesh.vertices;
        int vertexCount = initialLocalVertices.Length;
        dynamicLocalVertices = new Vector3[vertexCount];

        positions = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
        predictedPositions = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
        velocities = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
        invMasses = new NativeArray<float>(vertexCount, Allocator.Persistent);

        Vector3 centerOfMass = Vector3.zero;
        for (int i = 0; i < vertexCount; i++)
        {
            positions[i] = transform.TransformPoint(initialLocalVertices[i]);
            invMasses[i] = 1.0f;
            centerOfMass += positions[i];
        }
        centerOfMass /= vertexCount;

        if (anchorTopVertices)
        {
            float highestY = -Mathf.Infinity;
            for (int i = 0; i < vertexCount; i++)
                if (positions[i].y > highestY) highestY = positions[i].y;

            float lockThreshold = highestY - (highestY - centerOfMass.y) * 0.2f;
            for (int i = 0; i < vertexCount; i++)
                if (positions[i].y > lockThreshold) invMasses[i] = 0.0f;
        }

        int[] triangles = simulationMesh.triangles;
        List<int> listA = new List<int>();
        List<int> listB = new List<int>();
        List<float> listDist = new List<float>();
        List<float> listComp = new List<float>();
        HashSet<long> uniqueEdges = new HashSet<long>();

        // 1. Structural Distance Constraints
        for (int i = 0; i < triangles.Length; i += 3)
        {
            AddUniqueEdge(triangles[i], triangles[i + 1], listA, listB, listDist, listComp, uniqueEdges, surfaceCompliance);
            AddUniqueEdge(triangles[i + 1], triangles[i + 2], listA, listB, listDist, listComp, uniqueEdges, surfaceCompliance);
            AddUniqueEdge(triangles[i + 2], triangles[i], listA, listB, listDist, listComp, uniqueEdges, surfaceCompliance);
        }

        // 2. Distance-Based Bending Constraints
        Dictionary<string, int> edgeToOppositeVertex = new Dictionary<string, int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v0 = triangles[i]; int v1 = triangles[i + 1]; int v2 = triangles[i + 2];
            int[,] edges = new int[3, 2] { { v0, v1 }, { v1, v2 }, { v2, v0 } };
            int[] oppositeVertices = new int[3] { v2, v0, v1 };

            for (int e = 0; e < 3; e++)
            {
                int a = Mathf.Min(edges[e, 0], edges[e, 1]);
                int b = Mathf.Max(edges[e, 0], edges[e, 1]);
                string edgeKey = a + "_" + b;

                if (edgeToOppositeVertex.TryGetValue(edgeKey, out int tipB))
                {
                    int tipA = oppositeVertices[e];
                    AddUniqueEdge(tipA, tipB, listA, listB, listDist, listComp, uniqueEdges, surfaceCompliance * 4f);
                }
                else edgeToOppositeVertex[edgeKey] = oppositeVertices[e];
            }
        }

        // 3. Shape Core Volume Proxy
        for (int i = 0; i < vertexCount; i += 4)
        {
            Vector3 toCenter = centerOfMass - positions[i];
            float bestDot = -1f; int oppositeIndex = i;
            for (int j = 0; j < vertexCount; j += 8)
            {
                float dot = Vector3.Dot(toCenter.normalized, (positions[j] - centerOfMass).normalized);
                if (dot > bestDot) { bestDot = dot; oppositeIndex = j; }
            }
            if (i != oppositeIndex) AddUniqueEdge(i, oppositeIndex, listA, listB, listDist, listComp, uniqueEdges, volumeCompliance);
        }

        int edgeCount = listA.Count;
        constraintIndicesA = new NativeArray<int>(listA.ToArray(), Allocator.Persistent);
        constraintIndicesB = new NativeArray<int>(listB.ToArray(), Allocator.Persistent);
        constraintRestLengths = new NativeArray<float>(listDist.ToArray(), Allocator.Persistent);
        constraintCompliances = new NativeArray<float>(listComp.ToArray(), Allocator.Persistent);
        constraintLambdas = new NativeArray<float>(edgeCount, Allocator.Persistent);
    }

    private void AddUniqueEdge(int a, int b, List<int> listA, List<int> listB, List<float> listDist, List<float> listComp, HashSet<long> uniqueEdges, float comp)
    {
        int min = Mathf.Min(a, b); int max = Mathf.Max(a, b);
        long edgeKey = ((long)min << 32) | (uint)max;
        if (!uniqueEdges.Contains(edgeKey))
        {
            uniqueEdges.Add(edgeKey); listA.Add(min); listB.Add(max);
            listDist.Add(Vector3.Distance(positions[min], positions[max])); listComp.Add(comp);
        }
    }

    void FixedUpdate()
    {
        XPBDSimulationJob job = new XPBDSimulationJob
        {
            positions = this.positions,
            predictedPositions = this.predictedPositions,
            velocities = this.velocities,
            invMasses = this.invMasses,
            constraintIndicesA = this.constraintIndicesA,
            constraintIndicesB = this.constraintIndicesB,
            constraintRestLengths = this.constraintRestLengths,
            constraintCompliances = this.constraintCompliances,
            constraintLambdas = this.constraintLambdas,
            gravity = this.gravity,
            drag = this.drag,
            floorLevel = this.floorLevel,
            substeps = this.substeps,
            iterations = this.iterations,
            fixedDeltaTime = Time.fixedDeltaTime
        };

        JobHandle handle = job.Schedule();
        handle.Complete(); 

        // Update the low-res mesh representation if required for debugging layout
        if (GetComponent<MeshRenderer>() != null && GetComponent<MeshRenderer>().enabled)
        {
            for (int i = 0; i < positions.Length; i++)
                dynamicLocalVertices[i] = transform.InverseTransformPoint(positions[i]);
            simulationMesh.vertices = dynamicLocalVertices;
            simulationMesh.RecalculateNormals();
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct XPBDSimulationJob : IJob
    {
        public NativeArray<Vector3> positions;
        public NativeArray<Vector3> predictedPositions;
        public NativeArray<Vector3> velocities;
        [ReadOnly] public NativeArray<float> invMasses;
        [ReadOnly] public NativeArray<int> constraintIndicesA;
        [ReadOnly] public NativeArray<int> constraintIndicesB;
        [ReadOnly] public NativeArray<float> constraintRestLengths;
        [ReadOnly] public NativeArray<float> constraintCompliances;
        public NativeArray<float> constraintLambdas;

        public Vector3 gravity;
        public float drag;
        public float floorLevel;
        public int substeps;
        public int iterations;
        public float fixedDeltaTime;

        public void Execute()
        {
            float h = fixedDeltaTime / substeps;

            for (int step = 0; step < substeps; step++)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (invMasses[i] > 0.0f)
                    {
                        velocities[i] += gravity * h;
                        velocities[i] *= Unity.Mathematics.math.exp(-drag * h);
                        predictedPositions[i] = positions[i] + velocities[i] * h;
                    }
                    else predictedPositions[i] = positions[i];
                }

                for (int c = 0; c < constraintLambdas.Length; c++) constraintLambdas[c] = 0.0f;

                for (int iter = 0; iter < iterations; iter++)
                {
                    for (int i = 0; i < constraintRestLengths.Length; i++)
                    {
                        int idxA = constraintIndicesA[i]; int idxB = constraintIndicesB[i];
                        float wA = invMasses[idxA]; float wB = invMasses[idxB];
                        float wSum = wA + wB; if (wSum <= 0.0f) continue;

                        Vector3 n = predictedPositions[idxB] - predictedPositions[idxA];
                        float d = n.magnitude; if (d < 0.0001f) continue;
                        n /= d;

                        float C = d - constraintRestLengths[i];
                        float alpha = constraintCompliances[i] / (h * h);
                        float deltaLambda = (-C - alpha * constraintLambdas[i]) / (wSum + alpha);

                        predictedPositions[idxA] -= wA * deltaLambda * n;
                        predictedPositions[idxB] += wB * deltaLambda * n;
                        constraintLambdas[i] += deltaLambda;
                    }
                }

                for (int i = 0; i < predictedPositions.Length; i++)
                {
                    if (predictedPositions[i].y < floorLevel)
                    {
                        Vector3 p = predictedPositions[i]; p.y = floorLevel; predictedPositions[i] = p;
                    }
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    velocities[i] = (predictedPositions[i] - positions[i]) / h;
                    positions[i] = predictedPositions[i];
                }
            }
        }
    }

    void OnDestroy()
    {
        if (positions.IsCreated) positions.Dispose();
        if (predictedPositions.IsCreated) predictedPositions.Dispose();
        if (velocities.IsCreated) velocities.Dispose();
        if (invMasses.IsCreated) invMasses.Dispose();
        if (constraintIndicesA.IsCreated) constraintIndicesA.Dispose();
        if (constraintIndicesB.IsCreated) constraintIndicesB.Dispose();
        if (constraintRestLengths.IsCreated) constraintRestLengths.Dispose();
        if (constraintCompliances.IsCreated) constraintCompliances.Dispose();
        if (constraintLambdas.IsCreated) constraintLambdas.Dispose();
    }
}