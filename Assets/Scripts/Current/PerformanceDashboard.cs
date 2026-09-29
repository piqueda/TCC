using UnityEngine;
using TMPro;

public class PerformanceDashboard : MonoBehaviour
{
    [Header("References")]
    public Visualizer physicsSolver;
    public TextMeshProUGUI statsText;

    [Header("Update Interval")]
    public float updateInterval = 0.2f;

    private float timeAccumulator = 0f;
    private int frameCount = 0;
    private float fps = 0;
    private float frameTimeMs = 0f;

    void Update()
    {
        if(physicsSolver == null || statsText == null) return;

        timeAccumulator += Time.unscaledDeltaTime;
        frameCount++;

        if (timeAccumulator >= updateInterval)
        {
            fps = frameCount / timeAccumulator;
            frameTimeMs = (timeAccumulator / frameCount) * 1000f;
            UpdateDashboardText();
            timeAccumulator = 0f;
            frameCount = 0;
        }
    }

    private void UpdateDashboardText()
    {
        int tetCount = physicsSolver.uniqueTetrahedra.IsCreated ? physicsSolver.uniqueTetrahedra.Length : 0;
        int vertCount = physicsSolver.positions.IsCreated ? physicsSolver.positions.Length : 0;
        int grabCount = physicsSolver.grabbedVertices.IsCreated ? physicsSolver.grabbedVertices.Length : 0;

        string fpsColor = fps >= 70f ? "#00FF00" : (fps >= 45f ? "#FFFF00" : "#FF0000");

        statsText.text = 
            $"<size=120%><b>PERFORMANCE & PHYSICS METRICS</b></size>\n" +
            $"-------------------------------------------\n" +
            $"<b>Framerate:</b> <color={fpsColor}>{fps:F1} FPS</color> ({frameTimeMs:F2} ms)\n" +
            $"<b>Physics Substeps:</b> {physicsSolver.substeps}\n" +
            $"<b>Solver Iterations:</b> {physicsSolver.iterations}\n" +
            $"<b>Mesh Nodes:</b> {vertCount:N0} vertices\n" +
            $"<b>Physics Volume:</b> {tetCount:N0} tetrahedra\n" +
            $"<b>Active Interaction:</b> {grabCount} grabbed nodes\n" +
            $"<b>Compliance:</b> {physicsSolver.devCompliance}\n";
    }
}
