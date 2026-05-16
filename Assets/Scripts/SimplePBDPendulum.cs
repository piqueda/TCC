using UnityEngine;

public class SimplePBDPendulum : MonoBehaviour
{
    [Header("Visual References")]
    public Transform anchorVisual; // Drag "Anchor" sphere here
    public Transform bobVisual;   // Drag "Bob" sphere here
    private LineRenderer line;

    [Header("Simulation Settings")]
    public float gravity = -9.81f;
    public float restLength = 2.0f;
    [Range(0, 1)] public float stiffness = 1.0f;
    public int iterations = 5;

    // Physics State
    private Vector3 pos0, pos1;
    private Vector3 vel1;
    private float invMass0 = 0f; // Fixed
    private float invMass1 = 1f; // Dynamic

    void Start()
    {
        // Set initial physics positions to where the spheres are currently
        pos0 = anchorVisual.position;
        pos1 = bobVisual.position;

        // Setup LineRenderer to see the "rope" in Game view
        line = gameObject.GetComponent<LineRenderer>();
        if (line == null) line = gameObject.AddComponent<LineRenderer>();
        line.startWidth = 0.05f;
        line.endWidth = 0.05f;
        line.positionCount = 2;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Update anchor position to match the GameObject in case you move it
        pos0 = transform.position;

        // 1. Predict
        vel1 += Vector3.up * gravity * dt;
        Vector3 pred0 = pos0;
        Vector3 pred1 = pos1 + vel1 * dt;

        // 2. Solve
        for (int i = 0; i < iterations; i++)
        {
            if (SolveDistanceConstraint(pred0, invMass0, pred1, invMass1, restLength, stiffness, out Vector3 c0, out Vector3 c1))
            {
                pred0 += c0;
                pred1 += c1;
            }
        }

        // 3. Update Velocity and Position
        vel1 = (pred1 - pos1) / dt;
        pos1 = pred1;
        pos0 = pred0;

        // 4. Update Visuals
        anchorVisual.position = pos0;
        bobVisual.position = pos1;
        line.SetPosition(0, pos0);
        line.SetPosition(1, pos1);
    }

    public bool SolveDistanceConstraint(Vector3 p0, float invMass0, Vector3 p1, float invMass1, float restLength, float stiffness, out Vector3 corr0, out Vector3 corr1)
    {
        corr0 = corr1 = Vector3.zero;
        float wSum = invMass0 + invMass1;
        if (wSum <= 0.0f) return false;
        Vector3 n = p1 - p0;
        float d = n.magnitude;
        if (d < 0.0001f) return false;
        n /= d;
        Vector3 corr = stiffness * n * (d - restLength) / wSum;
        corr0 = invMass0 * corr;
        corr1 = -invMass1 * corr;
        return true;
    }
}