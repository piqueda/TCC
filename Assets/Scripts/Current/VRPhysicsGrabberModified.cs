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

    [Header("Hand Settings")]
    [Tooltip("CRITICAL: Set to 0 for Left Hand, and 1 for Right Hand in the inspector!")]
    public int handIndex = 0;

    [Header("Grab Settings")]
    public float grabRadius = 0.05f;

    [Header("Visual Debugging")]
    public bool showGrabSphere = true;

    private bool isGrabbing = false;
    /*
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
    */
    void Update()
    {
        if(physicsSolver == null) return;
        // Wait for the solver to initialize its arrays
        if(!physicsSolver.handTargetPositions.IsCreated) return;

        // 1. ALWAYS update where this hand is in the liver's local space
        Vector3 localHandPos = physicsSolver.transform.InverseTransformPoint(transform.position);
        physicsSolver.handTargetPositions[handIndex] = (float3)localHandPos;

        bool triggerPressed = (grabAction.action != null && grabAction.action.IsPressed()) || Input.GetKey(KeyCode.Space);

        // 2. Handle Grab and Release
        if(!isGrabbing && triggerPressed)
        {
            TryGrabLiver((float3)localHandPos);
        }
        else if (isGrabbing && !triggerPressed)
        {
            ReleaseLiver();
        }
    }

    private void TryGrabLiver(float3 localHandPos)
    {
        var positions = physicsSolver.positions;
        if(!positions.IsCreated) return;

        bool grabbedAnything = false;

        for(int i = 0; i < positions.Length; i++)
        {
            float dst = math.distance(localHandPos, positions[i]);
            if(dst < grabRadius)
            {
                physicsSolver.grabbedVertices.Add(new Visualizer.GrabbedVertex
                {
                    index = i,
                    localOffset = positions[i] - localHandPos,
                    handIndex = this.handIndex // Tell the solver WHICH hand grabbed this
                });
                grabbedAnything = true;
            }
        }

        if(grabbedAnything)
        {
            isGrabbing = true;
            Debug.Log($"[VR Grabber] Hand {handIndex} agarrou!");
        }
    }

    private void ReleaseLiver()
    {
        if (!physicsSolver.grabbedVertices.IsCreated) return;

        // Loop backwards to safely remove ONLY this hand's vertices
        for (int i = physicsSolver.grabbedVertices.Length - 1; i >= 0; i--)
        {
            if (physicsSolver.grabbedVertices[i].handIndex == this.handIndex)
            {
                physicsSolver.grabbedVertices.RemoveAt(i);
            }
        }

        isGrabbing = false;
        Debug.Log($"[VR Grabber] Hand {handIndex} largou o fígado");
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGrabSphere) return;
        Gizmos.color = isGrabbing ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }
}
