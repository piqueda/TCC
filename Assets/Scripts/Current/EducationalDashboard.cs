using UnityEngine;

public class EducationalDashboard : MonoBehaviour
{
    [Header("References")]
    public Visualizer physicsSolver;
    public MeshRenderer visualMeshRenderer;

    public enum LiverCondition {Healthy, FattyLiver, Cirrhossis}

    public void SetLiverConditionm(int conditionIndex)
    {
        LiverCondition condition = (LiverCondition)conditionIndex;
        Material mat = visualMeshRenderer.material;

        switch (condition)
        {
            case LiverCondition.Healthy:
            mat.color = new Color(0.45f, 0.12f, 0.10f);
            physicsSolver.devCompliance = 0.001f;
            break;

            case LiverCondition.FattyLiver:
            mat.color = new Color(0.75f, 0.65f, 0.30f);
            physicsSolver.devCompliance = 0.002f;
            break;

            case LiverCondition.Cirrhossis:
            mat.color = new Color(0.35f, 0.28f, 0.22f);
            physicsSolver.devCompliance = 0.00015f;
            break;
        }

        Debug.Log($"[EducationalDashboard] Mudou para a condição {condition}. Nova compliance: {physicsSolver.devCompliance}");
    }
}
