using UnityEngine;
using System.Collections.Generic;

public class JellyXPBDSolver : MonoBehaviour
{
    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f,0);
    public float drag = 0.5f;
    public float compliance = 0.0001f;
    public int substeps = 10;
    public int iterations = 2;

    [Header("Cube Grid Settings")]
    public int gridSize = 4;
    public float spacing = 0.4f;
    public GameObject particleVisualPrefab;

    //Essential particle arrays
    private Vector3[] positions;
    private Vector3[] predictedPositions;
    private Vector3[] velocities;
    private float[] invMasses;

    //Distance constraint arrays
    private int[] constraintIndicesA;
    private int[] constraintIndicesB;
    private float[] constraintRestLengths;
    private float[] constraintLambdas;
    private float[] constraintCompliances;

    private Transform[] visualTransforms;

    private int GetIndex(int x, int y, int z)
    {
        return x + (y * gridSize) + (z * gridSize * gridSize);
    }
    void Start()
    {
        int totalParticles = gridSize * gridSize * gridSize;

        // 1. Allocate Particle Arrays
        positions = new Vector3[totalParticles];
        predictedPositions = new Vector3[totalParticles];
        velocities = new Vector3[totalParticles];
        invMasses = new float[totalParticles];
        visualTransforms = new Transform[totalParticles];

        // 2. Generate 3D Grid of Particles
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    int idx = GetIndex(x, y, z);

                    // Offset grid positions from the solver's origin
                    Vector3 localPos = new Vector3(x, y, z) * spacing;
                    positions[idx] = transform.position + localPos;
                    predictedPositions[idx] = positions[idx];
                    velocities[idx] = Vector3.zero;

                    invMasses[idx] = 1.0f;

                    // Spawn visual spheres since LineRenderer can't easily draw a 3D grid
                    if (particleVisualPrefab != null)
                    {
                        GameObject go = Instantiate(particleVisualPrefab, positions[idx], Quaternion.identity);
                        visualTransforms[idx] = go.transform;
                    }
                }
            }
        }

        // 3. Dynamic Lists to gather constraints safely
        List<int> listA = new List<int>();
        List<int> listB = new List<int>();
        List<float> listDist = new List<float>();

        // 4. Procedural Lattice Wiring Loop
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    int current = GetIndex(x, y, z);

                    // --- TYPE 1: STRUCTURAL CONSTRAINTS ---
                    if (x + 1 < gridSize) AddConstraint(current, GetIndex(x + 1, y, z), spacing, listA, listB, listDist);
                    if (y + 1 < gridSize) AddConstraint(current, GetIndex(x, y + 1, z), spacing, listA, listB, listDist);
                    if (z + 1 < gridSize) AddConstraint(current, GetIndex(x, y, z + 1), spacing, listA, listB, listDist);

                    // --- TYPE 2: SHEAR CONSTRAINTS (Face Diagonals) ---
                    float diagonalDist = spacing * Mathf.Sqrt(2f);
                    if (x + 1 < gridSize && y + 1 < gridSize)
                    {
                        AddConstraint(GetIndex(x, y, z),     GetIndex(x + 1, y + 1, z), diagonalDist, listA, listB, listDist);
                        AddConstraint(GetIndex(x + 1, y, z), GetIndex(x, y + 1, z),     diagonalDist, listA, listB, listDist);
                    }
                    if (x + 1 < gridSize && z + 1 < gridSize)
                    {
                        AddConstraint(GetIndex(x, y, z),     GetIndex(x + 1, y, z + 1), diagonalDist, listA, listB, listDist);
                        AddConstraint(GetIndex(x + 1, y, z), GetIndex(x, y, z + 1),     diagonalDist, listA, listB, listDist);
                    }
                    if (y + 1 < gridSize && z + 1 < gridSize)
                    {
                        AddConstraint(GetIndex(x, y, z),     GetIndex(x, y + 1, z + 1), diagonalDist, listA, listB, listDist);
                        AddConstraint(GetIndex(x, y + 1, z), GetIndex(x, y, z + 1),     diagonalDist, listA, listB, listDist);
                    }

                    // --- TYPE 3: BENDING CONSTRAINTS (Skip neighbors for volumetric strength) ---
                    float skipDist = spacing * 2.0f;
                    if (x + 2 < gridSize) AddConstraint(current, GetIndex(x + 2, y, z), skipDist, listA, listB, listDist);
                    if (y + 2 < gridSize) AddConstraint(current, GetIndex(x, y + 2, z), skipDist, listA, listB, listDist);
                    if (z + 2 < gridSize) AddConstraint(current, GetIndex(x, y, z + 2), skipDist, listA, listB, listDist);
                }
            }
        }

        // 5. Bake compiled lists back into Flat Arrays!
        int totalConstraints = listA.Count;
        constraintIndicesA = listA.ToArray();
        constraintIndicesB = listB.ToArray();
        constraintRestLengths = listDist.ToArray();
        constraintLambdas = new float[totalConstraints];
        constraintCompliances = new float[totalConstraints];
    }

    private void AddConstraint(int a, int b, float restLen, List<int> listA, List<int> listB, List<float> listDist)
    {
        listA.Add(a);
        listB.Add(b);
        listDist.Add(restLen);
    }

    void FixedUpdate()
    {
        float h = Time.fixedDeltaTime / substeps;

        for (int step = 0; step < substeps; step++)
        {
            // 1. Predict Positions
            for (int i = 0; i < positions.Length; i++)
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

            // Reset Multipliers
            for (int c = 0; c < constraintLambdas.Length; c++)
            {
                constraintLambdas[c] = 0.0f;
                constraintCompliances[c] = compliance;
            }

            // 2. Solve Constraints Loop
            for (int iter = 0; iter < iterations; iter++)
            {
                SolveDistanceConstraintsDOD(h);
            }

            float floorLevel = 0.0f; // The Y coordinate of your floor
            for (int i = 0; i < predictedPositions.Length; i++)
            {
                if (predictedPositions[i].y < floorLevel)
                {
                    predictedPositions[i].y = floorLevel; // Stop at floor
                    
                    // Optional friction: slow down horizontal sliding on impact
                    velocities[i].x *= 0.8f;
                    velocities[i].z *= 0.8f;
                }
            }
            // 3. Finalize States
            for (int i = 0; i < positions.Length; i++)
            {
                velocities[i] = (predictedPositions[i] - positions[i]) / h;
                positions[i] = predictedPositions[i];
            }
        }
    }

    void Update()
    {
        // Update visual transform positions 
        for (int i = 0; i < positions.Length; i++)
        {
            if (visualTransforms[i] != null)
            {
                visualTransforms[i].position = positions[i];
            }
        }
    }

    // Notice: This is identical to your chain loop! The math engine doesn't care about geometry shapes.
    private void SolveDistanceConstraintsDOD(float h)
    {
        for (int i = 0; i < constraintRestLengths.Length; i++)
        {
            int idxA = constraintIndicesA[i];
            int idxB = constraintIndicesB[i];

            float wA = invMasses[idxA];
            float wB = invMasses[idxB];
            float wSum = wA + wB;

            if (wSum <= 0.0f) continue;

            Vector3 n = predictedPositions[idxB] - predictedPositions[idxA];
            float d = n.magnitude;
            if (d < 0.0001f) continue;
            n /= d;

            float C = d - constraintRestLengths[i];
            float alpha = constraintCompliances[i] / (h * h);
            float deltaLambda = (-C - alpha * constraintLambdas[i]) / (wSum + alpha);

            predictedPositions[idxA] -= wA * deltaLambda * n;
            predictedPositions[idxB] += wB * deltaLambda * n;
            constraintLambdas[i] += deltaLambda;
        }
    }

    // Draws the internal skeleton in the editor view so you can look at it
    private void OnDrawGizmos()
    {
        if (positions == null || constraintIndicesA == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < constraintIndicesA.Length; i++)
        {
            Gizmos.DrawLine(positions[constraintIndicesA[i]], positions[constraintIndicesB[i]]);
        }
    }
}
