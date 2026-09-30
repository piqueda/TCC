using UnityEngine;
using TMPro;

public class EducationalDashboard : MonoBehaviour
{
    [Header("References")]
    public Visualizer physicsSolver;
    public MeshRenderer visualMeshRenderer;
    public TextMeshProUGUI descriptionText;

    public enum LiverCondition {Healthy, FattyLiver, Cirrhossis}
    private void Start()
    {
        SetLiverCondition(0); 
    }
    public void SetLiverCondition(int conditionIndex)
    {
        LiverCondition condition = (LiverCondition)conditionIndex;
        Material mat = visualMeshRenderer.material;

        switch (condition)
        {
            case LiverCondition.Healthy:
            mat.color = new Color(0.45f, 0.12f, 0.10f);
            physicsSolver.ClearVelocities();
            physicsSolver.devCompliance = 0.001f;
            if (descriptionText != null)
            {
                descriptionText.text = 
                    "<b>Healthy Liver</b>\n" +
                    "A normal liver is soft, flexible, and full of healthy blood flow. Squeezing or pulling it feels squishy, and it snaps right back into shape.";
            }
            break;

            case LiverCondition.FattyLiver:
            mat.color = new Color(0.75f, 0.65f, 0.30f);
            physicsSolver.ClearVelocities();
            physicsSolver.devCompliance = 0.002f;
            if (descriptionText != null)
            {
                descriptionText.text = 
                    "<b>Fatty Liver</b>\n" +
                    "Extra fat builds up inside liver cells, giving it a pale yellow color. The tissue becomes slightly swollen, feeling heavier, softer, and more doughy.";
            }
            break;

            case LiverCondition.Cirrhossis:
            mat.color = new Color(0.35f, 0.28f, 0.22f);
            physicsSolver.ClearVelocities();
            physicsSolver.devCompliance = 0.00035f;
            if (descriptionText != null)
            {
                descriptionText.text = 
                    "<b>Cirrhosis (Severe Scarring)</b>\n" +
                    "Long-term damage creates tough scar tissue throughout the liver. The organ becomes dark, extremely stiff (nearly 10x harder), and barely deforms when grabbed.";
            }
            break;
        }

        Debug.Log($"[EducationalDashboard] Mudou para a condição {condition}. Nova compliance: {physicsSolver.devCompliance}");
    }
}
