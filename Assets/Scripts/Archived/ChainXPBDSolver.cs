using UnityEngine;

public class ChainXPBDSolver : MonoBehaviour
{
   [Header("Visual Components")]
   public Transform anchorVisual;
   public Transform bobVisual;
   private LineRenderer line;

   [Header("Simulation Settings")]
   public Vector3 gravity = new Vector3(0, -9.81f,0);
   public float drag = 0.5f;
   public float compliance = 0.0001f;
   public int substeps = 10;
   public int iterations = 1;

   [Header("Chain Settings")]
   public int particleCount = 15;
   public float segmentLength = 0.25f;

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


   void Start()
    {
        positions = new Vector3[particleCount];
        predictedPositions = new Vector3[particleCount];
        velocities = new Vector3[particleCount];
        invMasses = new float[particleCount];

        //Setting the "chain" up horizontally
        for(int i = 0; i < particleCount; i++)
        {
            positions[i] = transform.position + Vector3.right * (i * segmentLength);
            predictedPositions[i] = positions[i];
            velocities[i] = Vector3.zero;
            invMasses[i] = (i == 0) ? 0.0f : 1.0f;
        }

        int constraintCount = particleCount - 1;

        constraintIndicesA = new int[constraintCount];
        constraintIndicesB = new int[constraintCount];
        constraintRestLengths = new float[constraintCount];
        constraintLambdas = new float[constraintCount];
        constraintCompliances = new float[constraintCount];

        //Connecting the particles to make the chain
        for(int i = 0; i < constraintCount; i++)
        {
            constraintIndicesA[i] = i;
            constraintIndicesB[i] = i + 1;
            constraintRestLengths[i] = segmentLength;
        }

        line = gameObject.GetComponent<LineRenderer>();
        if (line == null) line = gameObject.AddComponent<LineRenderer>();
        line.startWidth = 0.05f; line.endWidth = 0.05f;
        line.positionCount = particleCount; 
    }

    void FixedUpdate()
    {
        positions[0] = transform.position;

        float h = Time.fixedDeltaTime / substeps;

        for(int step = 0; step < substeps; step++)
        {
            for(int i = 0; i < positions.Length; i++)
            {
                if(invMasses[i] > 0.0f)
                {
                    velocities[i] += gravity * h;
                    velocities[i] *= Mathf.Exp(-drag * h);
                    predictedPositions[i] = positions[i] + velocities[i] * h;
                }
                else
                {
                    predictedPositions[i] = positions[i];
                }
            }

            for(int c = 0; c < constraintLambdas.Length; c++)
            {
                constraintLambdas[c] = 0.0f;
                constraintCompliances[c] = compliance;
            }

            for (int iter = 0; iter < iterations; iter++)
            {
                SolveDistanceConstraintsDOD(h);
            }

            for (int i = 0; i < positions.Length; i++)
            {
                velocities[i] = (predictedPositions[i] - positions[i]) / h;
                positions[i] = predictedPositions[i];
            }
        }
    }

    void Update()
    {
        if (anchorVisual != null) anchorVisual.position = positions[0];
        
        if (bobVisual != null && positions.Length > 0) 
            bobVisual.position = positions[positions.Length - 1];
        
        // Render the smooth multi-segmented line
        if (line != null)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                line.SetPosition(i, positions[i]);
            }
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

            if (wSum <= 0.0f) continue; 

            Vector3 n = predictedPositions[idxB] - predictedPositions[idxA];
            float d = n.magnitude;
            if (d < 0.0001f) continue; 
            n /= d; 

            float C = d - constraintRestLengths[i];

            float alpha = constraintCompliances[i] / (h * h);

            float deltaLambda = (-C - alpha * constraintLambdas[i]) / (wSum + alpha);

            // Apply mass-weighted positional updates directly back into the flat state arrays
            predictedPositions[idxA] -= wA * deltaLambda * n;
            predictedPositions[idxB] += wB * deltaLambda * n;

            // Accumulate the multiplier
            constraintLambdas[i] += deltaLambda;
        }
    }
}
