using UnityEngine;
using Unity.Collections;

public class MeshDeformerBinder : MonoBehaviour
{
    [Header("Target Physics Source")]
    public BurstLiverXPBDSolver physicsCage;

    private MeshFilter visualMeshFilter;
    private Mesh visualMesh;
    private Vector3[] initialVisualVertices;
    private Vector3[] dynamicVisualVertices;

    private int[] bindIndicesA;
    private int[] bindIndicesB;
    private int[] bindIndicesC;
    private Vector3[] localOffsets;
    
    private bool isSuccessfullyBound = false; // Safety flag to prevent frame lag if initialization fails

    void Start()
    {
        if (physicsCage == null)
        {
            Debug.LogError("[MeshDeformerBinder] Requires a reference to a BurstLiverXPBDSolver component!");
            return;
        }

        // 1. SAFETY GUARD: If the physics script hasn't allocated native memory yet, abort safely.
        if (!physicsCage.positions.IsCreated || physicsCage.positions.Length == 0)
        {
            Debug.LogError("[MeshDeformerBinder] CRITICAL: Physics Cage positions are not initialized! " +
                           "Ensure BurstLiverXPBDSolver initializes in Awake() and its GameObject has a valid Mesh assigned.");
            return;
        }

        visualMeshFilter = GetComponent<MeshFilter>();
        if (visualMeshFilter == null || visualMeshFilter.sharedMesh == null) return;

        visualMesh = Instantiate(visualMeshFilter.sharedMesh);
        visualMeshFilter.mesh = visualMesh;

        initialVisualVertices = visualMesh.vertices;
        int highResVertexCount = initialVisualVertices.Length;
        dynamicVisualVertices = new Vector3[highResVertexCount];

        bindIndicesA = new int[highResVertexCount];
        bindIndicesB = new int[highResVertexCount];
        bindIndicesC = new int[highResVertexCount];
        localOffsets = new Vector3[highResVertexCount];

        NativeArray<Vector3> cageStartPositions = physicsCage.positions;

        for (int i = 0; i < highResVertexCount; i++)
        {
            Vector3 vWorld = transform.TransformPoint(initialVisualVertices[i]);
            int idxA = 0, idxB = 0, idxC = 0;
            float distA = Mathf.Infinity, distB = Mathf.Infinity, distC = Mathf.Infinity;

            for (int p = 0; p < cageStartPositions.Length; p++)
            {
                float d = Vector3.SqrMagnitude(vWorld - cageStartPositions[p]);
                if (d < distA)
                {
                    distC = distB; idxC = idxB;
                    distB = distA; idxB = idxA;
                    distA = d;     idxA = p;
                }
                else if (d < distB)
                {
                    distC = distB; idxC = idxB;
                    distB = d;     idxB = p;
                }
                else if (d < distC) { distC = d; idxC = p; }
            }

            bindIndicesA[i] = idxA; bindIndicesB[i] = idxB; bindIndicesC[i] = idxC;

            Vector3 pA = cageStartPositions[idxA];
            Vector3 pB = cageStartPositions[idxB];
            Vector3 pC = cageStartPositions[idxC];

            Vector3 u = (pB - pA).normalized;
            Vector3 w = Vector3.Cross(u, (pC - pA).normalized).normalized;
            if (w.sqrMagnitude < 0.001f) w = Vector3.up; 
            Vector3 v = Vector3.Cross(w, u).normalized;

            Vector3 relativePos = vWorld - pA;
            localOffsets[i] = new Vector3(Vector3.Dot(relativePos, u), Vector3.Dot(relativePos, v), Vector3.Dot(relativePos, w));
        }

        isSuccessfullyBound = true; // Binding complete! Safe to run frame updates.
    }

    void LateUpdate()
    {
        // 2. If setup failed, exit immediately. This completely stops the "swimming through mud" editor lag.
        if (!isSuccessfullyBound || physicsCage == null || !physicsCage.positions.IsCreated) return;

        NativeArray<Vector3> currentCagePositions = physicsCage.positions;

        for (int i = 0; i < dynamicVisualVertices.Length; i++)
        {
            Vector3 pA = currentCagePositions[bindIndicesA[i]];
            Vector3 pB = currentCagePositions[bindIndicesB[i]];
            Vector3 pC = currentCagePositions[bindIndicesC[i]];

            Vector3 u = (pB - pA).normalized;
            Vector3 w = Vector3.Cross(u, (pC - pA).normalized).normalized;
            if (w.sqrMagnitude < 0.001f) w = Vector3.up;
            Vector3 v = Vector3.Cross(w, u).normalized;

            Vector3 offset = localOffsets[i];
            Vector3 worldPos = pA + (u * offset.x) + (v * offset.y) + (w * offset.z);

            dynamicVisualVertices[i] = transform.InverseTransformPoint(worldPos);
        }

        visualMesh.vertices = dynamicVisualVertices;
        visualMesh.RecalculateNormals();
    }
}