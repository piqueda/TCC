using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class VolumeConstraintXPBDSolver : MonoBehaviour
{
    [Header("File Setup")]
    public string vtkFileName = "Patient 3 - liver_parenchyma - VTK.vtk";

    [Header("Scale Adjustment")]
    public float scaleFactor = 0.001f;
    
    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f,0);
    public float drag = 0.2f;
    public float edgeCompliance = 0.001f;
    public float volumeCompliance = 0.0f;
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
    private float[] edgeLambdas;

    private int[] uniqueTetrahedra;
    private float[] restVolumes;
    private float[] volumeLambdas;
    void Start()
    {
        //Setup do leitor de arquivo .vtk
        string path = Path.Combine(Application.streamingAssetsPath, vtkFileName);
        VTKLoader loader = new VTKLoader();
        
        if (!loader.Load(path, scaleFactor))
        {
            Debug.LogError($"[DistanceConstraintXPBDSolver] Falhou em carregar o arquivo");
        }

        //Número de vértices
        int vertexCount = loader.vertices.Length;
        Debug.Log($"[DistanceConstraintXPBDSolver] Foram carregados {vertexCount} vertices");

        positions = new Vector3[vertexCount];
        predictedPositions = new Vector3[vertexCount];
        velocities = new Vector3[vertexCount];
        invMasses =  new float[vertexCount];

        System.Array.Copy(loader.vertices, positions, vertexCount);

        for(int i = 0; i < vertexCount; i++)
        {
            velocities[i] = Vector3.zero;
            invMasses[i] = 1.0f;
        }

        //Número de arestas
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
        Debug.Log($"[DistanceConstraintXPBDSolver] Foram carregados {edgeCount} arestas unicas");
        restLengths = new float[edgeCount];
        edgeLambdas = new float[edgeCount];

        for(int i = 0; i < edgeCount; i++)
        {
            Edge edge = uniqueEdges[i];
            restLengths[i] = Vector3.Distance(positions[edge.vA], positions[edge.vB]);
        }


        //Número de volumes
        int tetraCount = loader.tetrahedra.Length/4;
        Debug.Log($"[DistanceConstraintXPBDSolver] Foram carregados {tetraCount} tetraedros");
        uniqueTetrahedra = new int[loader.tetrahedra.Length];
        System.Array.Copy(loader.tetrahedra, uniqueTetrahedra, loader.tetrahedra.Length);

        restVolumes = new float[tetraCount];
        volumeLambdas = new float[tetraCount];

        for(int i = 0; i < tetraCount; i++)
        {
            int id0 = uniqueTetrahedra[i*4 + 0];
            int id1 = uniqueTetrahedra[i*4 + 1];
            int id2 = uniqueTetrahedra[i*4 + 2];
            int id3 = uniqueTetrahedra[i*4 + 3];

            Vector3 p0 = positions[id0];
            Vector3 p1 = positions[id1];
            Vector3 p2 = positions[id2];
            Vector3 p3 = positions[id3];

            /*
            float volume = Mathf.Abs(Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p3 - p0))/6f;
            restVolumes[i] = volume;
            */

            float rawVolume = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p3 - p0) / 6f;
            if (rawVolume < 0f)
            {
                int temp = uniqueTetrahedra[i * 4 + 1];
                uniqueTetrahedra[i * 4 + 1] = uniqueTetrahedra[i * 4 + 2];
                uniqueTetrahedra[i * 4 + 2] = temp;

                 rawVolume = -rawVolume;
            }
            restVolumes[i] = rawVolume;
        }
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

    // Update is called once per frame
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

           System.Array.Clear(edgeLambdas, 0, edgeLambdas.Length);
           System.Array.Clear(volumeLambdas, 0, volumeLambdas.Length);

            for(int i = 0; i < iterations; i++)
            {
                SolveDistanceConstraints(h);
                SolveVolumeConstraints(h);
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

            float deltaLambda = (-constraintEval - alpha * edgeLambdas[e]) / (wSum + alpha);
            edgeLambdas[e] += deltaLambda;

            // Instantly push the predicted coordinates closer or further apart
            Vector3 correction = deltaLambda * direction;
            predictedPositions[idxA] += wA * correction;
            predictedPositions[idxB] -= wB * correction;
        }
    }

    private void SolveVolumeConstraints(float h)
    {
        float alpha = volumeCompliance / (h * h);
        int tetraCount = uniqueTetrahedra.Length/4;

        for(int i = 0; i < tetraCount; i++)
        {
            int id0 = uniqueTetrahedra[i*4 + 0];
            int id1 = uniqueTetrahedra[i*4 + 1];
            int id2 = uniqueTetrahedra[i*4 + 2];
            int id3 = uniqueTetrahedra[i*4 + 3];

            float w0 = invMasses[id0];
            float w1 = invMasses[id1];
            float w2 = invMasses[id2];
            float w3 = invMasses[id3];

            if(w0 + w1 + w2 + w3 <= 0f)
                continue;

            Vector3 p0 = predictedPositions[id0];
            Vector3 p1 = predictedPositions[id1];
            Vector3 p2 = predictedPositions[id2];
            Vector3 p3 = predictedPositions[id3];
            
            Vector3 d1 = p1 - p0;
            Vector3 d2 = p2 - p0;
            Vector3 d3 = p3 - p0;

            float currentVolume = Vector3.Dot(Vector3.Cross(d1, d2), d3) / 6f;
            float constraintEval = currentVolume - restVolumes[i];

            Vector3 grad3 = Vector3.Cross(d1, d2) / 6f;
            Vector3 grad2 = Vector3.Cross(d3, d1) / 6f;
            Vector3 grad1 = Vector3.Cross(d2, d3) / 6f;
            Vector3 grad0 = -(grad1 + grad2 + grad3);
            
            /*
            float currentVolume = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p3 - p0)/6f;
            float constraintEval = currentVolume - restVolumes[i];

            
            Vector3 grad0 = Vector3.Cross(p2 - p1, p3 - p1)/6f;
            Vector3 grad1 = Vector3.Cross(p0 - p2, p3 - p2)/6f;
            Vector3 grad2 = Vector3.Cross(p1 - p0, p3 - p0)/6f;
            Vector3 grad3 = Vector3.Cross(p1 - p2, p0 - p2)/6f;
            

            Vector3 grad0 = Vector3.Cross(p2 - p1, p3 - p1) / 6f;
            Vector3 grad1 = Vector3.Cross(p3 - p2, p0 - p2) / 6f;
            Vector3 grad2 = Vector3.Cross(p0 - p3, p1 - p3) / 6f;
            Vector3 grad3 = Vector3.Cross(p1 - p0, p2 - p0) / 6f;
            */

            float gMassSum = (w0 * grad0.sqrMagnitude) + (w1 * grad1.sqrMagnitude) + (w2 * grad2.sqrMagnitude) + (w3 * grad3.sqrMagnitude);

            if (gMassSum <= 1e-24f) continue;

            float deltaLambda = (-constraintEval - alpha * volumeLambdas[i]) / (gMassSum + alpha);

            volumeLambdas[i] += deltaLambda;
            predictedPositions[id0] += w0 * deltaLambda * grad0;
            predictedPositions[id1] += w1 * deltaLambda * grad1;
            predictedPositions[id2] += w2 * deltaLambda * grad2;
            predictedPositions[id3] += w3 * deltaLambda * grad3;
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
