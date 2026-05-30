using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class DistanceConstraintXPBDSolver : MonoBehaviour
{
    [Header("File Setup")]
    public string vtkFileName = "Patient 3 - liver_parenchyma - VTK.vtk";

    [Header("Scale Adjustment")]
    public float scaleFactor = 0.001f;
    
    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f,0);
    public float drag = 0.2f;
    public float edgeCompliance = 0.001f;
    public int substeps = 10;
    public int iterations = 2;

    [Header("Floor Collision")]
    public float floorLevel = -1.5f;

    [Header("Visuals Properties")]
    public float pointRadius = 0.01f;
    public Color pointColor = Color.cyan;
    public Color edgeColor = Color.yellow;

    private struct Edge
    {
        public int vA;
        public int vB;
        public Edge(int a, int b) { vA = a; vB = b;}
    }

    private Vector3[] positions;
    private Vector3[] predictedPositions;
    private Vector3[] velocities;
    private float[] invMasses;

    private List<Edge> uniqueEdges;
    private float[] restLengths;
    private float[] lambdas;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, vtkFileName);
        VTKLoader loader = new VTKLoader();
        
        if (!loader.Load(path, scaleFactor))
        {
            Debug.LogError($"[DistanceConstraintXPBDSolver] Falhou em carregar o arquivo");
        }

        int vertexCount = loader.vertices.Length;
        Debug.Log($"[DistanceConstraintXPBDSolver] Foram carregados {vertexCount} vertices");

        positions = new Vector3[vertexCount];
        predictedPositions = new Vector3[vertexCount];
        velocities = new Vector3[vertexCount];
        invMasses =  new float[vertexCount];

        System.Array.Copy(loader.vertices, positions, vertexCount);

        float maxY = float.MinValue;
        for (int i = 0; i < vertexCount; i++)
        {
            if (positions[i].y > maxY) maxY = positions[i].y;
        }

        for(int i = 0; i < vertexCount; i++)
        {
            velocities[i] = Vector3.zero;
            if (positions[i].y > maxY - 0.015f)
            {
                invMasses[i] = 0.0f; 
            }
            else
            {
                invMasses[i] = 1.0f; 
            }
        }

        HashSet<long> edgeTracker = new HashSet<long>();
        uniqueEdges = new List<Edge>();

        for(int i = 0; i < loader.tetrahedra.Length; i+= 4)
        {
            if(i + 3 >= loader.tetrahedra.Length)
                break;
            int i0 = loader.tetrahedra[i];
            int i1 = loader.tetrahedra[i + 1];
            int i2 = loader.tetrahedra[i + 2];
            int i3 = loader.tetrahedra[i + 3];

            TryAddEdge(i0, i1, edgeTracker);
            TryAddEdge(i0, i2, edgeTracker);
            TryAddEdge(i0, i3, edgeTracker);
            TryAddEdge(i1, i2, edgeTracker);
            TryAddEdge(i1, i3, edgeTracker);
            TryAddEdge(i2, i3, edgeTracker);
        }

        int edgeCount = uniqueEdges.Count;
        restLengths = new float[edgeCount];
        lambdas = new float[edgeCount];

        for(int i = 0; i < edgeCount; i++)
        {
            Edge edge = uniqueEdges[i];
            restLengths[i] = Vector3.Distance(positions[edge.vA], positions[edge.vB]);
        }

        Debug.Log($"[DistanceConstraintXPBDSolver] Setup Concluido!");
    }

    private void TryAddEdge(int a, int b, HashSet<long> tracker)
    {
        int min = Mathf.Min(a,b);
        int max = Mathf.Max(a,b);
        long edgeKey = ((long)min << 32) | (uint)max;

        if (!tracker.Contains(edgeKey))
        {
            tracker.Add(edgeKey);
            uniqueEdges.Add(new Edge(min,max));
        }
    }

    void Update()
    {
        if(positions == null)
            return;

        float dt = Time.deltaTime;
        if(dt > 0.03f)
            dt = 0.03f;
        
        float h = dt/substeps;

        for(int step = 0; step < substeps; step++)
        {
            for(int i = 0; i < positions.Length; i++)
            {
                if (invMasses[i] > 0.0f)
                {
                    velocities[i] += gravity * h;
                    velocities[i] *= Mathf.Exp(-drag * h);
                    predictedPositions[i] = positions[i] + velocities[i] * h;
                }
                else
                {
                    predictedPositions[i] = positions[i]; // Anchors stay put
                }
            }

            for(int i = 0; i < lambdas.Length; i++)
            {
                lambdas[i] = 0f;
            }

            for(int i = 0; i < iterations; i++)
            {
                SolveDistanceConstraints(h);
            }

            for(int i = 0; i < predictedPositions.Length; i++)
            {
                if (predictedPositions[i].y < floorLevel)
                {
                    predictedPositions[i].y = floorLevel;
                }
            }

            for(int i = 0; i < positions.Length; i++)
            {
                velocities[i] = (predictedPositions[i] - positions[i]) / h;
                positions[i] = predictedPositions[i];
            }
        }
    }

    private void SolveDistanceConstraints(float h)
    {
        float alpha = edgeCompliance / (h * h); 

        for (int e = 0; e < uniqueEdges.Count; e++)
        {
            Edge edge = uniqueEdges[e];
            int idxA = edge.vA;
            int idxB = edge.vB;

            float wA = invMasses[idxA];
            float wB = invMasses[idxB];
            float wSum = wA + wB;
            if (wSum <= 0f) continue; 

            Vector3 posA = predictedPositions[idxA];
            Vector3 posB = predictedPositions[idxB];

            Vector3 direction = posA - posB;
            float currentLength = direction.magnitude;
            if (currentLength < 0.0001f) continue;
            direction /= currentLength; 

            float constraintEval = currentLength - restLengths[e];

            float deltaLambda = (-constraintEval - alpha * lambdas[e]) / (wSum + alpha);
            lambdas[e] += deltaLambda;

            // Instantly push the predicted coordinates closer or further apart
            Vector3 correction = deltaLambda * direction;
            predictedPositions[idxA] += wA * correction;
            predictedPositions[idxB] -= wB * correction;
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || positions == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = pointColor;
        for (int i = 0; i < positions.Length; i++)
        {
            Gizmos.DrawSphere(positions[i], pointRadius);
        }

        if (uniqueEdges != null)
        {
            Gizmos.color = edgeColor;
            for (int e = 0; e < uniqueEdges.Count; e++)
            {
                Gizmos.DrawLine(positions[uniqueEdges[e].vA], positions[uniqueEdges[e].vB]);
            }
        }
    }
}
