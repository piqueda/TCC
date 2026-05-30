using UnityEngine;
using System.IO;

public class VTKTester : MonoBehaviour
{
    [Header("File Setup")]
    public string vtkFileName = "Patient 3 - liver_parenchyma - VTK.vtk";

    [Header("Scale Adjustment")]
    public float scaleFactor = 0.001f;
    
    [Header("Visual Properties")]
    public bool drawPoints = true;
    public bool drawWireframe = false; 
    public float pointSize = 0.01f;

    private VTKLoader loader;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, vtkFileName);
        loader = new VTKLoader();
        
        if (loader.Load(path, scaleFactor))
        {
            Debug.Log($"Foram carregados {loader.vertices.Length} vertices e {loader.tetrahedra.Length / 4} tetraedros.");
        }
        else
        {
            loader = null; // Limpa o loader para não causar erros nos Gizmos
        }
    }

    void OnDrawGizmos()
    {
        if (loader == null || loader.vertices == null) return;

        // Equiparação com as propriedades do GameObject
        Gizmos.matrix = transform.localToWorldMatrix; 

        // Cria os pontos
        if (drawPoints)
        {
            Gizmos.color = Color.cyan;
            foreach (Vector3 v in loader.vertices)
            {
                Gizmos.DrawSphere(v, pointSize);
            }
        }

        // Cria os tetraedros como uma wireframe
        if (drawWireframe && loader.tetrahedra != null)
        {
            Gizmos.color = Color.yellow;
            
            for (int i = 0; i < loader.tetrahedra.Length; i += 4)
            {
                Vector3 v0 = loader.vertices[loader.tetrahedra[i]];
                Vector3 v1 = loader.vertices[loader.tetrahedra[i + 1]];
                Vector3 v2 = loader.vertices[loader.tetrahedra[i + 2]];
                Vector3 v3 = loader.vertices[loader.tetrahedra[i + 3]];

                // Draw the base triangle
                Gizmos.DrawLine(v0, v1);
                Gizmos.DrawLine(v1, v2);
                Gizmos.DrawLine(v2, v0);
                
                // Draw the lines to the top point
                Gizmos.DrawLine(v0, v3);
                Gizmos.DrawLine(v1, v3);
                Gizmos.DrawLine(v2, v3);
            }
        }
    }
}