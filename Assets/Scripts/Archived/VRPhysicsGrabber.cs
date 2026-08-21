/*
using UnityEngine;
using Unity.Mathematics;

public class VRPhysicsGrabber : MonoBehaviour
{
    [Header("References")]
    public Visualizer physicsSolver;

    [Header("Grab Settings")]
    public float grabRadius = 0.1f;

    private bool isGrabbing = false;
    void Update()
    {
        if(physicsSolver == null) return;
        bool triggerPressed = Input.GetKeyDown(KeyCode.JoystickButton14) || Input.GetKey(KeyCode.Space);

        if(!isGrabbing && triggerPressed)
        {
            TryGrabLiver();
        }
        else if (isGrabbing)
        {
            if(Input.GetKey(KeyCode.JoystickButton14) || Input.GetKey(KeyCode.Space))
            {
                physicsSolver.grabTargetPosition = transform.position;
            }
            else
            {
                physicsSolver.grabbedVertexIndex = -1;
                isGrabbing = false;
                Debug.Log("[VR Grabber] Largou o figado");
            }
        }
    }

    private void TryGrabLiver()
    {
        var positions = physicsSolver.positions;
        if(!positions.IsCreated) return;

        float3 handPos = (float3)transform.position;
        int closestIdx = -1;
        float minDst = grabRadius;

        for(int i = 0; i < positions.Length; i++)
        {
            float dst = math.distance(handPos, positions[i]);
            if(dst < minDst)
            {
                minDst = dst;
                closestIdx = i;
            }
        }

        if(closestIdx != -1)
        {
            physicsSolver.grabbedVertexIndex = closestIdx;
            physicsSolver.grabTargetPosition = transform.position;
            isGrabbing = true;
            Debug.Log($"[VR Grabber] Indice da partícula agarrada: {closestIdx}");
        }
    }
}
*/
