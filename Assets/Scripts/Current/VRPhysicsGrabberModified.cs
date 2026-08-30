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

    void Update()
    {
        if(physicsSolver == null) return;
        if(!physicsSolver.handTargetPositions.IsCreated) return;

        Vector3 localHandPos = physicsSolver.transform.InverseTransformPoint(transform.position);
        physicsSolver.handTargetPositions[handIndex] = (float3)localHandPos;

        bool triggerPressed = (grabAction.action != null && grabAction.action.IsPressed()) || Input.GetKey(KeyCode.Space);

        if(!isGrabbing && triggerPressed)
        {
            TryGrabLiver((float3)localHandPos);
        }
        else if (isGrabbing && !triggerPressed)
        {
            ReleaseLiver();
        }
    }

    /*
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
                    handIndex = this.handIndex 
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
    */

    /*
    private void TryGrabLiver(float3 localHandPos)
    {
        var positions = physicsSolver.positions;
        if (!positions.IsCreated) return;

        int closestIndex = -1;
        float closestDist = grabRadius;

        // Search physics nodes in solver local coordinates
        for (int i = 0; i < positions.Length; i++)
        {
            float dst = math.distance(localHandPos, positions[i]);
            if (dst < closestDist)
            {
                closestDist = dst;
                closestIndex = i;
            }
        }

        if (closestIndex >= 0)
        {
            physicsSolver.grabbedVertices.Add(new Visualizer.GrabbedVertex
            {
                index = closestIndex,
                localOffset = positions[closestIndex] - localHandPos,
                handIndex = this.handIndex 
            });

            isGrabbing = true;
            Debug.Log($"[VR Grabber] Hand {handIndex} agarrou o nó {closestIndex}!");
        }
        else
        {
            Debug.Log($"[VR Grabber] Hand {handIndex} errou: nenhum nó a menos de {grabRadius} unidades.");
        }
    }
    */

    private void TryGrabLiver(float3 localHandPos)
    {
        var positions = physicsSolver.positions;
        if (!positions.IsCreated) return;

        bool grabbedAny = false;

        // Grab ALL physics nodes inside grabRadius to move a volume patch of tissue
        for (int i = 0; i < positions.Length; i++)
        {
            float dst = math.distance(localHandPos, positions[i]);
            if (dst < grabRadius)
            {
                physicsSolver.grabbedVertices.Add(new Visualizer.GrabbedVertex
                {
                    index = i,
                    localOffset = positions[i] - localHandPos,
                    handIndex = this.handIndex
                });
                grabbedAny = true;
            }
        }

        if (grabbedAny)
        {
            isGrabbing = true;
            Debug.Log($"[VR Grabber] Hand {handIndex} grabbed tissue region ({physicsSolver.grabbedVertices.Length} nodes)!");
        }
        else
        {
            Debug.Log($"[VR Grabber] Hand {handIndex} missed grab: no physics nodes within {grabRadius} units.");
        }
    }

    private void ReleaseLiver()
    {
        if (!physicsSolver.grabbedVertices.IsCreated) return;

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
