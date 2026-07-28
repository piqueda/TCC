using UnityEngine;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class VRPhysicsGrabberModified : MonoBehaviour
{
    [Header("References")]
    public Visualizer physicsSolver;

    [Header("Input")]
    [Tooltip("Assign the Select or Activate action from XRI Default Input Actions")]
    public InputActionProperty grabAction;

    [Header("Grab Settings")]
    public float grabRadius = 0.05f;

    [Header("Visual Debugging")]
    public bool showGrabSphere = true;

    private bool isGrabbing = false;
    void Update()
    {
        if(physicsSolver == null) return;

        bool triggerPressed = (grabAction.action != null && grabAction.action.IsPressed()) || Input.GetKey(KeyCode.Space);

        if(!isGrabbing && triggerPressed)
        {
            TryGrabLiver();
        }
        else if (isGrabbing)
        {
            if (triggerPressed)
            {
                Vector3 localHandPos = physicsSolver.transform.InverseTransformPoint(transform.position);
                physicsSolver.handLocalTargetPosition = (float3)localHandPos;
            }
            else
            {
                physicsSolver.grabbedVertices.Clear();
                isGrabbing = false;
                Debug.Log("[VR Grabber] Largou o fígado");
            }
        }
    }

    private void TryGrabLiver()
    {
        var positions = physicsSolver.positions;
        if(!positions.IsCreated) return;
        physicsSolver.grabbedVertices.Clear();
        Vector3 localHandPos = physicsSolver.transform.InverseTransformPoint(transform.position);
        float3 localHandPosF3 = (float3)localHandPos;
        for(int i = 0; i < positions.Length; i++)
        {
            float dst = math.distance(localHandPosF3, positions[i]);
            if(dst < grabRadius)
            {
                physicsSolver.grabbedVertices.Add(new Visualizer.GrabbedVertex
                {
                    index = i,
                    localOffset = positions[i] - localHandPosF3
                });
            }
        }

        if(physicsSolver.grabbedVertices.Length > 0)
        {
            physicsSolver.handLocalTargetPosition = localHandPosF3;
            isGrabbing = true;
            Debug.Log($"[VR Grabber] Agarrou {physicsSolver.grabbedVertices.Length} vertices");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGrabSphere) return;
        Gizmos.color = isGrabbing ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }
}
