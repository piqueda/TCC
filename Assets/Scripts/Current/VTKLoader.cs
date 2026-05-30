using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using System.Globalization;

public class VTKLoader
{
    public Vector3[] vertices;
    public int[] tetrahedra;

    public bool Load(string filePath, float scaleFactor = 0.001f)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("Arquivo VTK nao encontrado em: " + filePath);
            return false;
        }

        // Lê o arquivo inteiro e retira linhas em branco
        string text = File.ReadAllText(filePath);
        string[] tokens = text.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        List<Vector3> vertexList = new List<Vector3>();
        List<int> tetList = new List<int>();

        int i = 0;
        while (i < tokens.Length)
        {
            if (tokens[i] == "POINTS")
            {
                // Número de pontos
                // O formato é: POINTS [numPoints] [dataType]
                int numPoints = int.Parse(tokens[i + 1]);
                
                // Pula os espaços ocupados por POINTS, [numPoints] e [dataType]
                i += 3;

                for (int p = 0; p < numPoints; p++)
                {
                    // Coleta de coordenadas X, Y e Z (Culture.InvariantCulture serve para ler decimais e notação científica)
                    float x = float.Parse(tokens[i], CultureInfo.InvariantCulture);
                    float y = float.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
                    float z = float.Parse(tokens[i + 2], CultureInfo.InvariantCulture);
                    
                    vertexList.Add(new Vector3(x, y, z));
                    
                    // Avança 3 tokens para o próximo vértice
                    i += 3;
                }
                continue;
            }
            else if (tokens[i] == "CELLS")
            {
                //Número de células
                // O formato é: CELLS [numCells] [totalNumberOfIntegers]
                int numCells = int.Parse(tokens[i + 1]);
                
                // Pula os espaços ocupados por CELLS, [numCells] e [totalNumberOfIntegers]
                i += 3;

                for (int c = 0; c < numCells; c++)
                {
                    // O primeiro token de uma célula representa o número de vértices que ela possui 
                    int vertexCount = int.Parse(tokens[i]);
                    
                    // Só adiciona à lista se for um tetraedro
                    if (vertexCount == 4)
                    {
                        tetList.Add(int.Parse(tokens[i + 1]));
                        tetList.Add(int.Parse(tokens[i + 2]));
                        tetList.Add(int.Parse(tokens[i + 3]));
                        tetList.Add(int.Parse(tokens[i + 4]));
                    }
                    
                    // Avança o número de vértices + cada vértice individual da célula
                    i += (vertexCount + 1);
                }
                continue; 
            }
            
            // Avança caso a token não for POINTS ou CELLS
            i++; 
        }

        //Código para centralização em (0,0,0)
        if (vertexList.Count > 0)
        {
            Vector3 minBounds = vertexList[0];
            Vector3 maxBounds = vertexList[0];

            for (int v = 1; v < vertexList.Count; v++)
            {
                minBounds = Vector3.Min(minBounds, vertexList[v]);
                maxBounds = Vector3.Max(maxBounds, vertexList[v]);
            }

            Vector3 centerOffset = (minBounds + maxBounds) / 2f;

            for (int v = 0; v < vertexList.Count; v++)
            {
                vertexList[v] = (vertexList[v] -centerOffset) * scaleFactor;
            }

            Debug.Log($"[VTKLoader] O figado centralizado e escalado por {scaleFactor}");
        }
        
        vertices = vertexList.ToArray();
        tetrahedra = tetList.ToArray();
        return true;
    }
}