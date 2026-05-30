using UnityEngine;

public class PendulumXPBDSolver : MonoBehaviour
{

    [Header("Visual Components")]
    public Transform anchorVisual;
    public Transform bobVisual;
    private LineRenderer line;

    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f, 0);
    public float drag = 0.5f;
    public float restLength = 2.0f;

    [Tooltip("Compliance at 0 is perfectly stiff and grows stretchy as you increase it")]
    public float compliance = 0.0001f;
    [Tooltip("XPBD tends to perform better with more substeps.")]
    public int substeps = 10;
    [Tooltip("According to Muller best to keep iteration count low")]
    public int iterations = 1;

    /// <summary>
    /// Data
    /// </summary>

    //Particle Arrays: each particle is represented in the array
    private Vector3[] positions;
    private Vector3[] predictedPositions;
    private Vector3[] velocities;
    private float[] invMasses;

    //Distance Constraint Arrays: arrays concerning distance calculation
    private int[] constraintIndicesA; //position of particle A
    private int[] constraintIndicesB; // position of particle B
    private float[] constraintRestLengths; // distance between particles A and B
    private float[] constraintLambdas; //lambdas
    private float[] constraintCompliances; //compliance of XPBD

    void Start()
    {
        // 1. Allocate the flat arrays for 2 particles
        positions = new Vector3[2];
        predictedPositions = new Vector3[2];
        velocities = new Vector3[2];
        invMasses = new float[2];

        // Setup Particle 0 (The Anchor)
        positions[0] = transform.position;
        invMasses[0] = 0.0f; // 0 Inverse Mass = Infinite Mass (Static Anchor)

        // Setup Particle 1 (The Swinging Bob)
        positions[1] = transform.position + Vector3.right * restLength;
        invMasses[1] = 1.0f; // 1.0 Inverse Mass = 1kg Dynamic Object

        // 2. Allocate the flat arrays for 1 distance constraint
        constraintIndicesA = new int[1];
        constraintIndicesB = new int[1];
        constraintRestLengths = new float[1];
        constraintLambdas = new float[1];
        constraintCompliances = new float[1];

        // Define Constraint 0 (Connects Particle 0 to Particle 1)
        constraintIndicesA[0] = 0;
        constraintIndicesB[0] = 1;
        constraintRestLengths[0] = restLength;

        // 3. Setup LineRenderer for a quick visual representation
        line = gameObject.GetComponent<LineRenderer>();
        if (line == null) line = gameObject.AddComponent<LineRenderer>();
        line.startWidth = 0.05f; line.endWidth = 0.05f; line.positionCount = 2;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        positions[0] = transform.position;

        // Calculate the decoupled time step for our substeps
        float h = Time.fixedDeltaTime / substeps;

        // --- THE MAIN SUBSTEP LOOP ---
        for (int step = 0; step < substeps; step++)
        {
            // 1. Predict Positions (Explicit Euler step)
            for (int i = 0; i < positions.Length; i++)
            {
                if (invMasses[i] > 0.0f) // If the particle is dynamic
                {
                    velocities[i] += gravity * h;
                    velocities[i] *= Mathf.Exp(-drag * h);
                    predictedPositions[i] = positions[i] + velocities[i] * h;
                }
                else
                {
                    predictedPositions[i] = positions[i]; // Static anchor doesn't move
                }
            }

            // CRITICAL XPBD STEP: Reset Lagrange Multipliers at the start of each substep
            for (int c = 0; c < constraintLambdas.Length; c++)
            {
                constraintLambdas[c] = 0.0f;
                constraintCompliances[c] = compliance; // Sync compliance from Inspector
            }

            // 2. Solver Constraint Iteration Loop
            for (int iter = 0; iter < iterations; iter++)
            {
                SolveDistanceConstraintsDOD(h);
            }

            // 3. Finalize States (Update velocity and positions)
            for (int i = 0; i < positions.Length; i++)
            {
                velocities[i] = (predictedPositions[i] - positions[i]) / h;
                positions[i] = predictedPositions[i];
            }
        }
    }

    void Update()
    {
        // Simply read the results computed by FixedUpdate and move the graphics!
        if (anchorVisual != null) anchorVisual.position = positions[0];
        if (bobVisual != null) bobVisual.position = positions[1];
        
        if (line != null)
        {
            line.SetPosition(0, positions[0]);
            line.SetPosition(1, positions[1]);
        }
    }

    private void SolveDistanceConstraintsDOD(float h)
    {
        // Loop over the flat constraint arrays sequentially
        for (int i = 0; i < constraintRestLengths.Length; i++)
        {
            int idxA = constraintIndicesA[i];
            int idxB = constraintIndicesB[i];

            float wA = invMasses[idxA];
            float wB = invMasses[idxB];
            float wSum = wA + wB;

            if (wSum <= 0.0f) continue; // Both particles are static, skip

            // Calculate current geometric distance between the predicted positions
            Vector3 n = predictedPositions[idxB] - predictedPositions[idxA];
            float d = n.magnitude;
            if (d < 0.0001f) continue; // Avoid NaN/Division-by-zero if overlapping
            n /= d; // Direct normalization

            // Constraint Function: C(p) = currentDistance - restLength
            float C = d - constraintRestLengths[i];

            float alpha = constraintCompliances[i] / (h * h);

            // The Core XPBD Formula
            float deltaLambda = (-C - alpha * constraintLambdas[i]) / (wSum + alpha);

            // Apply mass-weighted positional updates directly back into the flat state arrays
            predictedPositions[idxA] -= wA * deltaLambda * n;
            predictedPositions[idxB] += wB * deltaLambda * n;

            // Accumulate the multiplier
            constraintLambdas[i] += deltaLambda;
        }
    }
}
