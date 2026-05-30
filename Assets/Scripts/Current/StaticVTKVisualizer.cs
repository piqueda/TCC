using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class StaticVTKVisualizer : MonoBehaviour
{
    [Header("File Setup")]
    public string vtkFileName = "Patient 3 - liver_parenchyma - VTK.vtk";

    [Header("Scale Adjustment")]
    public float scaleFactor = 0.001f;

    [Header("Visual Properties")]
    public bool drawPoints = true;
    public bool drawWireframe = true;
    [Range(0.001f, 0.5f)] public float pointRadius = 0.01f;
    public Color pointColor = Color.cyan;
    public Color wireframeColor = Color.yellow;

    private Vector3[] dynamicVertices;
    private List<int[]> tetrahedraCells;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, vtkFileName);
        VTKLoader loader = new VTKLoader();
        
        if (loader.Load(path, scaleFactor))
        {
            Debug.Log($"Foram carregados {loader.vertices.Length} vertices e {loader.tetrahedra.Length / 4} tetraedros.");
        }

        if (loader.vertices == null || loader.vertices.Length == 0)
        {
            Debug.LogError("[StaticVTK] Arquivo carregado mas nao possui vertices.");
            return;
        }

        dynamicVertices = new Vector3[loader.vertices.Length];
        System.Array.Copy(loader.vertices, dynamicVertices, loader.vertices.Length);

        tetrahedraCells = new List<int[]>();
        if(loader.tetrahedra != null)
        {
            for(int i = 0; i < loader. tetrahedra.Length; i += 4)
            {
                if(i + 3 >= loader.tetrahedra.Length)
                    break;
                tetrahedraCells.Add(new int[]{
                    loader.tetrahedra[i],
                    loader.tetrahedra[i+1],
                    loader.tetrahedra[i+2],
                    loader.tetrahedra[i+3]
                });
            }
        }

        Debug.Log($"[StaticVTK] Exibindo {dynamicVertices.Length} vertices e {tetrahedraCells.Count} tetraedros.");
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || dynamicVertices == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        if (drawPoints)
        {
            Gizmos.color = pointColor;
            for (int i = 0; i < dynamicVertices.Length; i++)
            {
                Gizmos.DrawSphere(dynamicVertices[i], pointRadius);
            }
        }

        if (drawWireframe && tetrahedraCells != null)
        {
            Gizmos.color = wireframeColor;
            foreach (int[] cell in tetrahedraCells)
            {
                Vector3 v0 = dynamicVertices[cell[0]];
                Vector3 v1 = dynamicVertices[cell[1]];
                Vector3 v2 = dynamicVertices[cell[2]];
                Vector3 v3 = dynamicVertices[cell[3]];

                // Construct baseline cage lines
                Gizmos.DrawLine(v0, v1); Gizmos.DrawLine(v1, v2); Gizmos.DrawLine(v2, v0);
                Gizmos.DrawLine(v0, v3); Gizmos.DrawLine(v1, v3); Gizmos.DrawLine(v2, v3);
            }
        }
    }
}
