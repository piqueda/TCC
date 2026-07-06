using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;


public class FEMSolver : MonoBehaviour
{
    [Header("File Setup")]
    public string vtkFileName = "Patient 3 - liver_parenchyma - VTK.vtk";

    [Header("Scale Adjustment")]
    public float scaleFactor = 0.001f;
    
    [Header("Simulation Settings")]
    public Vector3 gravity = new Vector3(0, -9.81f,0);
    public float drag = 0.2f;
    public float edgeCompliance = 0.001f;
    public float volumeCompliance = 0.0f;
    public int substeps = 10;
    public int iterations = 2;
    public float friction = 0.9f;

    [Header("Neo-Hookean Material Settings")]
    public float devCompliance = 0.001f;
    public float density = 1000f;

    [Header("Floor Collision")]
    public float floorLevel = -1.5f;

    [Header("Visuals Properties")]
    public float pointRadius = 0.002f;
    public Color pointColor = Color.cyan;
    public Color edgeColor = Color.yellow;

    private struct Edge
    {
        public int vA;
        public int vB;
        public Edge(int a, int b) {vA = a; vB = b;}
    }

    private NativeArray<float3> positions;
    private NativeArray<float3> predictedPositions;
    private NativeArray<float3> velocities;
    private NativeArray<float> invMasses;

    private NativeArray<Edge> uniqueEdges;
    private NativeArray<float> restLengths;
    private NativeArray<float> edgeLambdas;
    
    private NativeArray<int> uniqueTetrahedra;
    private NativeArray<float> restVolumes;
    private NativeArray<float> volumeLambdas;

    public struct InsideMatrix3x3
    {
        public float m00, m01, m02;
        public float m10, m11, m12;
        public float m20, m21, m22;
    }
    private NativeArray<InsideMatrix3x3> invRestPoses;
    private NativeArray<float> invRestVolumes;

    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, vtkFileName);
        VTKLoader loader = new VTKLoader();
        
        if (!loader.Load(path, scaleFactor))
        {
            Debug.LogError($"[FEMSolver] Falhou em carregar o arquivo");
        }

        int vertexCount = loader.vertices.Length;
        Debug.Log($"[FEMSolver] Foram carregados {vertexCount} vertices");
        
        positions = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        predictedPositions = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        velocities = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        invMasses = new NativeArray<float>(vertexCount, Allocator.Persistent);

        for(int i = 0; i < vertexCount; i++)
        {
            positions[i] = loader.vertices[i];
            velocities[i] = float3.zero;
            invMasses[i] = 0.0f;
        }

        HashSet<long> edgeTracker = new HashSet<long>();
        List<Edge> tempEdges = new List<Edge>();
        
        for(int i = 0; i < loader.tetrahedra.Length; i+= 4)
        {
            if(i + 3 >= loader.tetrahedra.Length)
                break;
            int i0 = loader.tetrahedra[i];
            int i1 = loader.tetrahedra[i + 1];
            int i2 = loader.tetrahedra[i + 2];
            int i3 = loader.tetrahedra[i + 3];

            TryAddEdge(i0, i1, edgeTracker, tempEdges);
            TryAddEdge(i0, i2, edgeTracker, tempEdges);
            TryAddEdge(i0, i3, edgeTracker, tempEdges);
            TryAddEdge(i1, i2, edgeTracker, tempEdges);
            TryAddEdge(i1, i3, edgeTracker, tempEdges);
            TryAddEdge(i2, i3, edgeTracker, tempEdges);
        }

        int edgeCount = tempEdges.Count;
        Debug.Log($"[FEMSolver] Foram carregados {edgeCount} arestas unicas");
        uniqueEdges = new NativeArray<Edge>(edgeCount, Allocator.Persistent);
        //restLengths = new NativeArray<float>(edgeCount, Allocator.Persistent);
        //edgeLambdas = new NativeArray<float>(edgeCount, Allocator.Persistent);

        for(int i = 0; i < edgeCount; i++)
        {
            uniqueEdges[i] = tempEdges[i];
            //restLengths[i] = math.distance(positions[tempEdges[i].vA], positions[tempEdges[i].vB]);
        }

        int tetraCount = loader.tetrahedra.Length/4;
        Debug.Log($"[FEMSolver] Foram carregados {tetraCount} tetraedros");
        uniqueTetrahedra = new NativeArray<int>(loader.tetrahedra.Length, Allocator.Persistent);
        for(int i = 0; i < loader.tetrahedra.Length; i++)
        {
            uniqueTetrahedra[i] = loader.tetrahedra[i];
        }

        //restVolumes = new NativeArray<float>(tetraCount, Allocator.Persistent);
        //volumeLambdas = new NativeArray<float>(tetraCount, Allocator.Persistent);

        invRestPoses = new NativeArray<InsideMatrix3x3>(tetraCount, Allocator.Persistent);
        invRestVolumes = new NativeArray<float>(tetraCount, Allocator.Persistent);

        for(int i = 0; i < tetraCount; i++)
        {
            int id0 = uniqueTetrahedra[i * 4 + 0];
            int id1 = uniqueTetrahedra[i * 4 + 1];
            int id2 = uniqueTetrahedra[i * 4 + 2];
            int id3 = uniqueTetrahedra[i * 4 + 3];

            float3 p0 = positions[id0];
            float3 p1 = positions[id1];
            float3 p2 = positions[id2];
            float3 p3 = positions[id3];

            float3x3 J = new float3x3(p1 - p0, p2 - p0, p3 - p0);
            float V = math.determinant(J)/6.0f;

            /*
            float rawVolume = math.dot(math.cross(p1 - p0, p2 - p0), p3 - p0)/6f;
            if(rawVolume < 0f)
            {
                int temp = uniqueTetrahedra[i * 4 + 1];
                uniqueTetrahedra[i * 4 + 1] = uniqueTetrahedra[i * 4 + 2];
                uniqueTetrahedra[i * 4 + 2] = temp;

                rawVolume = -rawVolume;
            }
            restVolumes[i] = rawVolume;
            */

            if (V < 0f)
            {
                int temp = uniqueTetrahedra[i * 4 + 1];
                uniqueTetrahedra[i * 4 + 1] = uniqueTetrahedra[i * 4 + 2];
                uniqueTetrahedra[i * 4 + 2] = temp;

                p1 = positions[uniqueTetrahedra[i * 4 + 1]];
                p2 = positions[uniqueTetrahedra[i * 4 + 2]];
                J = new float3x3(p1 - p0, p2 - p0, p3 - p0);
                V = -V;
            }

            float pm = (V / 4.0f) * density;
            invMasses[id0] += pm;
            invMasses[id1] += pm;
            invMasses[id2] += pm;
            invMasses[id3] += pm;

            float3x3 invJ = math.inverse(J);
            InsideMatrix3x3 ir;
            ir.m00 = invJ[0][0];
            ir.m01 = invJ[1][0];
            ir.m02 = invJ[2][0];
            ir.m10 = invJ[0][1];
            ir.m11 = invJ[1][1];
            ir.m12 = invJ[2][1];
            ir.m20 = invJ[0][2];
            ir.m21 = invJ[1][2];
            ir.m22 = invJ[2][2];
            
            invRestPoses[i] = ir;
            invRestVolumes[i] = 1.0f/V;
        }
        
        for (int i = 0; i < vertexCount; i++)
        {
            if (invMasses[i] > 0.0f)
            {
                invMasses[i] = 1.0f / invMasses[i]; 
            }
            else
            {
                invMasses[i] = 0.0f; 
            }
        }
        
    }

    private void TryAddEdge(int a, int b, HashSet<long> tracker, List<Edge> tempEdges)
    {
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        long edgeKey = ((long)min << 32) | (uint)max;

        if (!tracker.Contains(edgeKey))
        {
            tracker.Add(edgeKey);
            tempEdges.Add(new Edge(min, max));
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!positions.IsCreated) return;

        float dt = Time.deltaTime;
        if (dt > 0.03f) dt = 0.03f; // Clamp frame rate spikes

        float h = dt / substeps;

        for (int step = 0; step < substeps; step++)
        {
            IntegrateForcesJob integrateJob = new IntegrateForcesJob
            {
                positions = this.positions,
                predictedPositions = this.predictedPositions,
                velocities = this.velocities,
                invMasses = this.invMasses,
                gravity = (float3)this.gravity,
                drag = this.drag,
                h = h
            };
            JobHandle integrateHandle = integrateJob.Schedule(positions.Length, 64);

            /*
            SolveConstraintsJob solveJob = new SolveConstraintsJob
            {
                predictedPositions = this.predictedPositions,
                invMasses = this.invMasses,
                uniqueEdges = this.uniqueEdges,
                restLengths = this.restLengths,
                edgeLambdas = this.edgeLambdas,
                uniqueTetrahedra = this.uniqueTetrahedra,
                restVolumes = this.restVolumes,
                volumeLambdas = this.volumeLambdas,
                edgeCompliance = this.edgeCompliance,
                volumeCompliance = this.volumeCompliance,
                h = h,
                iterations = this.iterations
            };
            JobHandle solveHandle = solveJob.Schedule(integrateHandle);
            */

            SolveNeoHookeanConstraintsJob solveJob = new SolveNeoHookeanConstraintsJob
            {
              predictedPositions = this.predictedPositions,
              invMasses = this.invMasses,
              uniqueTetrahedra = this.uniqueTetrahedra,
              invRestPoses = this.invRestPoses,
              invRestVolumes = this.invRestVolumes,
              devCompliance = this.devCompliance,
              volumeCompliance = this.volumeCompliance,
              h = h,
              iterations = this.iterations  
            };
            JobHandle solveHandle = solveJob.Schedule(integrateHandle);

            FinalizePositionsJob finalizeJob = new FinalizePositionsJob
            {
                positions = this.positions,
                predictedPositions = this.predictedPositions,
                velocities = this.velocities,
                floorLevel = this.floorLevel,
                h = h,
                friction = this.friction
            };
            JobHandle finalizeHandle = finalizeJob.Schedule(positions.Length, 64, solveHandle);

            finalizeHandle.Complete();
        }
    }
    
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    private struct IntegrateForcesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> positions;
        public NativeArray<float3> predictedPositions;
        public NativeArray<float3> velocities;
        [ReadOnly] public NativeArray<float> invMasses;

        public float3 gravity;
        public float drag;
        public float h;

        public void Execute(int i)
        {
            if (invMasses[i] > 0.0f)
            {
                float3 vel = velocities[i] + gravity * h;
                vel *= math.exp(-drag * h);
                velocities[i] = vel;
                predictedPositions[i] = positions[i] + vel * h;
            }
            else
            {
                predictedPositions[i] = positions[i];
            }
        }
    }

    /*
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    private struct SolveConstraintsJob : IJob
    {
        public NativeArray<float3> predictedPositions;
        [ReadOnly] public NativeArray<float> invMasses;

        [ReadOnly] public NativeArray<Edge> uniqueEdges;
        [ReadOnly] public NativeArray<float> restLengths;
        public NativeArray<float> edgeLambdas;

        [ReadOnly] public NativeArray<int> uniqueTetrahedra;
        [ReadOnly] public NativeArray<float> restVolumes;
        public NativeArray<float> volumeLambdas;

        public float edgeCompliance;
        public float volumeCompliance;
        public float h;
        public int iterations;

        public void Execute()
        {
            for (int i = 0; i < edgeLambdas.Length; i++) edgeLambdas[i] = 0f;
            for (int i = 0; i < volumeLambdas.Length; i++) volumeLambdas[i] = 0f;

            float edgeAlpha = edgeCompliance / (h * h);
            float volumeAlpha = volumeCompliance / (h * h);
            int tetraCount = uniqueTetrahedra.Length / 4;

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int e = 0; e < uniqueEdges.Length; e++)
                {
                    Edge edge = uniqueEdges[e];
                    int idxA = edge.vA;
                    int idxB = edge.vB;

                    float wA = invMasses[idxA];
                    float wB = invMasses[idxB];
                    float wSum = wA + wB;
                    if (wSum <= 0f) continue;

                    float3 posA = predictedPositions[idxA];
                    float3 posB = predictedPositions[idxB];

                    float3 direction = posA - posB;
                    float currentLength = math.length(direction);
                    if (currentLength < 0.0001f) continue;
                    direction /= currentLength;

                    float constraintEval = currentLength - restLengths[e];

                    float deltaLambda = (-constraintEval - edgeAlpha * edgeLambdas[e]) / (wSum + edgeAlpha);
                    edgeLambdas[e] += deltaLambda;

                    float3 correction = deltaLambda * direction;
                    predictedPositions[idxA] += wA * correction;
                    predictedPositions[idxB] -= wB * correction;
                }

                for (int i = 0; i < tetraCount; i++)
                {
                    int id0 = uniqueTetrahedra[i * 4 + 0];
                    int id1 = uniqueTetrahedra[i * 4 + 1];
                    int id2 = uniqueTetrahedra[i * 4 + 2];
                    int id3 = uniqueTetrahedra[i * 4 + 3];

                    float w0 = invMasses[id0];
                    float w1 = invMasses[id1];
                    float w2 = invMasses[id2];
                    float w3 = invMasses[id3];

                    if (w0 + w1 + w2 + w3 <= 0f) continue;

                    float3 p0 = predictedPositions[id0];
                    float3 p1 = predictedPositions[id1];
                    float3 p2 = predictedPositions[id2];
                    float3 p3 = predictedPositions[id3];

                    float3 d1 = p1 - p0;
                    float3 d2 = p2 - p0;
                    float3 d3 = p3 - p0;

                    float currentVolume = math.dot(math.cross(d1, d2), d3) / 6f;
                    float constraintEval = currentVolume - restVolumes[i];

                    float3 grad3 = math.cross(d1, d2) / 6f;
                    float3 grad2 = math.cross(d3, d1) / 6f;
                    float3 grad1 = math.cross(d2, d3) / 6f;
                    float3 grad0 = -(grad1 + grad2 + grad3);

                    float gMassSum = (w0 * math.lengthsq(grad0)) + 
                                     (w1 * math.lengthsq(grad1)) + 
                                     (w2 * math.lengthsq(grad2)) + 
                                     (w3 * math.lengthsq(grad3));

                    float denom = gMassSum + volumeAlpha;
                    if(denom <= 1e-12f) continue;

                    float deltaLambda = (-constraintEval - volumeAlpha * volumeLambdas[i]) / (gMassSum + volumeAlpha);

                    volumeLambdas[i] += deltaLambda;
                    predictedPositions[id0] += w0 * deltaLambda * grad0;
                    predictedPositions[id1] += w1 * deltaLambda * grad1;
                    predictedPositions[id2] += w2 * deltaLambda * grad2;
                    predictedPositions[id3] += w3 * deltaLambda * grad3;
                }
            }
        }
    }
    */

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    private struct SolveNeoHookeanConstraintsJob : IJob
    {
        public NativeArray<float3> predictedPositions;
        [ReadOnly] public NativeArray<float> invMasses;

        [ReadOnly] public NativeArray<int> uniqueTetrahedra;
        [ReadOnly] public NativeArray<InsideMatrix3x3> invRestPoses;
        [ReadOnly] public NativeArray<float> invRestVolumes;

        public float devCompliance;
        public float volumeCompliance;
        public float h;
        public float iterations;
        
        public void Execute()
        {
            int tetraCount = uniqueTetrahedra.Length/4;
            float devAlpha = devCompliance/ (h * h);
            float volumeAlpha = volumeCompliance/ (h * h);

            float mu_over_lambda = (devCompliance > 0f) ? (volumeCompliance/devCompliance) : 0f;

            for(int iter = 0; iter < iterations; iter++)
            {
                for(int i = 0; i < tetraCount; i++)
                {
                    int id0 = uniqueTetrahedra[i * 4 + 0];
                    int id1 = uniqueTetrahedra[i * 4 + 1];
                    int id2 = uniqueTetrahedra[i * 4 + 2];
                    int id3 = uniqueTetrahedra[i * 4 + 3];

                    float w0 = invMasses[id0];
                    float w1 = invMasses[id1];
                    float w2 = invMasses[id2];
                    float w3 = invMasses[id3];

                    if (w0 + w1 + w2 + w3 <= 0f) continue;

                    InsideMatrix3x3 ir = invRestPoses[i];
                    float invV_rest = invRestVolumes[i];

                    float3 p0 = predictedPositions[id0];
                    float3 p1 = predictedPositions[id1];
                    float3 p2 = predictedPositions[id2];
                    float3 p3 = predictedPositions[id3];

                    float3 e1 = p1 - p0;
                    float3 e2 = p2 - p0;
                    float3 e3 = p3 - p0;

                    float3 f0 = e1 * ir.m00 + e2 * ir.m10 + e3 * ir.m20;
                    float3 f1 = e1 * ir.m01 + e2 * ir.m11 + e3 * ir.m21;
                    float3 f2 = e1 * ir.m02 + e2 * ir.m12 + e3 * ir.m22;

                    float r_s = math.sqrt(math.lengthsq(f0) + math.lengthsq(f1) + math.lengthsq(f2));
                    if(r_s > 1e-4f)
                    {
                        float r_s_inv = 1.0f/r_s;

                        float3 g1_dev = r_s_inv * (ir.m00 * f0 + ir.m01 * f1 + ir.m02 * f2);
                        float3 g2_dev = r_s_inv * (ir.m10 * f0 + ir.m11 * f1 + ir.m12 * f2);
                        float3 g3_dev = r_s_inv * (ir.m20 * f0 + ir.m21 * f1 + ir.m22 * f2);
                        float3 g0_dev = -(g1_dev + g2_dev + g3_dev);

                        float wSum_dev = (w0 * math.lengthsq(g0_dev)) + 
                                        (w1 * math.lengthsq(g1_dev)) + 
                                        (w2 * math.lengthsq(g2_dev)) + 
                                        (w3 * math.lengthsq(g3_dev));

                        if (wSum_dev > 1e-24f)
                        {
                            float alpha_dev = devAlpha * invV_rest;
                            float deltaLambda_dev = -r_s / (wSum_dev + alpha_dev);

                            predictedPositions[id0] += w0 * deltaLambda_dev * g0_dev;
                            predictedPositions[id1] += w1 * deltaLambda_dev * g1_dev;
                            predictedPositions[id2] += w2 * deltaLambda_dev * g2_dev;
                            predictedPositions[id3] += w3 * deltaLambda_dev * g3_dev;
                        }
                    }

                    p0 = predictedPositions[id0];
                    p1 = predictedPositions[id1];
                    p2 = predictedPositions[id2];
                    p3 = predictedPositions[id3];

                    e1 = p1 - p0;
                    e2 = p2 - p0;
                    e3 = p3 - p0;

                    f0 = e1 * ir.m00 + e2 * ir.m10 + e3 * ir.m20;
                    f1 = e1 * ir.m01 + e2 * ir.m11 + e3 * ir.m21;
                    f2 = e1 * ir.m02 + e2 * ir.m12 + e3 * ir.m22;

                    // Cofactor computations via cross products
                    float3 df0 = math.cross(f1, f2);
                    float3 df1 = math.cross(f2, f0);
                    float3 df2 = math.cross(f0, f1);

                    float3 g1_vol = ir.m00 * df0 + ir.m01 * df1 + ir.m02 * df2;
                    float3 g2_vol = ir.m10 * df0 + ir.m11 * df1 + ir.m12 * df2;
                    float3 g3_vol = ir.m20 * df0 + ir.m21 * df1 + ir.m22 * df2;
                    float3 g0_vol = -(g1_vol + g2_vol + g3_vol);

                    float wSum_vol = (w0 * math.lengthsq(g0_vol)) + 
                                    (w1 * math.lengthsq(g1_vol)) + 
                                    (w2 * math.lengthsq(g2_vol)) + 
                                    (w3 * math.lengthsq(g3_vol));

                    if (wSum_vol > 1e-24f)
                    {
                        float vol = math.dot(math.cross(f0, f1), f2);
                        float C_vol = vol - 1.0f - mu_over_lambda;

                        float alpha_vol = volumeAlpha * invV_rest;
                        float deltaLambda_vol = -C_vol / (wSum_vol + alpha_vol);

                        predictedPositions[id0] += w0 * deltaLambda_vol * g0_vol;
                        predictedPositions[id1] += w1 * deltaLambda_vol * g1_vol;
                        predictedPositions[id2] += w2 * deltaLambda_vol * g2_vol;
                        predictedPositions[id3] += w3 * deltaLambda_vol * g3_vol;
                    }
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    private struct FinalizePositionsJob : IJobParallelFor
    {
        public NativeArray<float3> positions;
        public NativeArray<float3> predictedPositions;
        public NativeArray<float3> velocities;
        
        public float floorLevel;
        public float h;
        public float friction;

        public void Execute(int i)
        {
            float3 predPos = predictedPositions[i];
            float3 oldPos = positions[i];

            // Floor Collision
            if (predPos.y < floorLevel)
            {
                predPos.y = floorLevel;

                float frictionFactor = math.saturate(friction * h * 60f);
                predPos.x = math.lerp(predPos.x, oldPos.x, friction);
                predPos.z = math.lerp(predPos.z, oldPos.z, friction);
            }

            predictedPositions[i] = predPos;
            float3 newVel = (predPos - oldPos)/h;
            velocities[i] = newVel * 0.99f;
            positions[i] = predPos;
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !positions.IsCreated) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = pointColor;
        
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 pos = (Vector3)positions[i];
            
            if (float.IsNaN(pos.x) || float.IsInfinity(pos.x)) continue; 
            
            Gizmos.DrawSphere(pos, pointRadius);
        }

        if (uniqueEdges.IsCreated)
        {
            Gizmos.color = edgeColor;
            for (int e = 0; e < uniqueEdges.Length; e++)
            {
                Vector3 pA = (Vector3)positions[uniqueEdges[e].vA];
                Vector3 pB = (Vector3)positions[uniqueEdges[e].vB];
                
                if (float.IsNaN(pA.x) || float.IsNaN(pB.x)) continue;
                
                Gizmos.DrawLine(pA, pB);
            }
        }
    }

    void OnDestroy()
    {
        if (positions.IsCreated) positions.Dispose();
        if (predictedPositions.IsCreated) predictedPositions.Dispose();
        if (velocities.IsCreated) velocities.Dispose();
        if (invMasses.IsCreated) invMasses.Dispose();
        
        if (uniqueEdges.IsCreated) uniqueEdges.Dispose();
        //if (restLengths.IsCreated) restLengths.Dispose();
        //if (edgeLambdas.IsCreated) edgeLambdas.Dispose();
        
        if (uniqueTetrahedra.IsCreated) uniqueTetrahedra.Dispose();
        //if (restVolumes.IsCreated) restVolumes.Dispose();
        //if (volumeLambdas.IsCreated) volumeLambdas.Dispose();

        if(invRestPoses.IsCreated) invRestPoses.Dispose();
        if(invRestVolumes.IsCreated) invRestVolumes.Dispose();
    }
}
