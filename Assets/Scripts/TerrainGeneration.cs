using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct GenerationOctave
{
    public float NoiseDistance;
    public float NoiseHeight;
    public bool Enabled;
}

public struct MeshData
{
    public Vector3[] Vertices;
    public int[] Tris;
    public Vector2[] Uvs;
    public Color[] Colours;

    public MeshData(List<Vector3> verts, List<int> tris, List<Vector2> uvs, Color[] colours)
    {
        Vertices = verts.ToArray();
        Tris = tris.ToArray();
        Uvs = uvs.ToArray();
        Colours = colours;
    }
}

[Serializable]
public struct LodPreset
{
    public float LodLevel;
    public int Range;
    public bool Enabled;
}

/// <summary>
/// TODO: Add world origin zeroing at player
/// </summary>
public class TerrainGeneration : MonoBehaviour {

    public static TerrainGeneration instance;
    public static Dictionary<Vector2Int, TerrainChunk> Chunks = new Dictionary<Vector2Int, TerrainChunk>(); // Probably change key to something better for performance
    public static Dictionary<float, MeshData> AllMeshData = new Dictionary<float, MeshData>(); // All mesh data for the LOD
    public static TerrainChunk[] AllChunks;
    public static Queue<TerrainChunk> ChunkPool = new Queue<TerrainChunk>(); // Pool ready for use
    public static Queue<TerrainChunk> ChunksAwaitingMeshApplying = new Queue<TerrainChunk>();
    public static Queue<TerrainChunk> ChunksAwaitingMeshRecaluation = new Queue<TerrainChunk>();
    public static Dictionary<Vector2Int, Mesh> CachedMeshData = new Dictionary<Vector2Int, Mesh>();
    [SerializeField]
    public GameObject ChunkPrefab;
    public GameObject ThreadManager;
    public Transform ChunksParent;
    public Transform WaterLayer;
    public bool IsMultiThreaded = true;
    public bool IsBlockTerrain;
    public bool IsShowingTerrainSyncing;
    public bool IsFixingTerrainSeams = true;
    public int ChunkSize; // Size of each chunk
    public int GridSize; // Size of the entire grid e.g 8 * 8
    public float BlockSize;
    public int Seed; // Seed for the noise
    public GenerationOctave[] Octaves; // The octaves for generation
    public LodPreset[] LodPresets;
    public float FarLodSize = 10f;
    public Gradient TerrainColours;
    public Color UnderWaterColour;

    public static Vector3 ChunkScale;
    private void Awake()
    {
        if (!instance) instance = this;
        Seed = /*-6394955; */UnityEngine.Random.Range(-9999, 9999); // Generate the random seed
        ChunkScale = new Vector3(ChunkSize / 10, 1, ChunkSize / 10); // Generate the chunk scale for the chunk object x,z size.
        GenerateLodLevels();
        CreatePool();
        if (IsMultiThreaded) Instantiate(ThreadManager);
        GenerateChunks(_updatedPosition);
        GenerateAllChunkNeighbours();
    }

    private readonly int _updatesPerFrame = 200; // Max chunks it can generate on a frame
    private readonly int _generateChunkNeighboursSteps = 2;
    private int _update;
    private void Update()
    {
        lock (ChunksAwaitingMeshApplying)
        {
            // When chunks are queued for re-generation run the below
            while (ChunksAwaitingMeshApplying.Count > 0)
            {
                if (_update >= _updatesPerFrame)
                {
                    _update = 0;
                    break;
                }
                TerrainChunk chunk = ChunksAwaitingMeshApplying.Dequeue();
                chunk.GenerateChunkNeighbours(_generateChunkNeighboursSteps);
                if (!IsMultiThreaded) chunk.GenerateChunkMesh(chunk.LODLevel, true);

                // Check if a chunk should generation or delete objects 
                if (MathUtility.CompareLodLevel(chunk.LODLevel, FarLodSize))
                {
                    ObjectManager.RemoveObjectsAtChunk(chunk);
                }
                else
                {
                    if (chunk.IsForcedObjectGeneration)
                    {
                        chunk.IsForcedObjectGeneration = false;
                        ObjectManager.PlaceObjectsAtChunk(chunk);
                    }
                }
                chunk.UpdateChunkMesh();
                _update++;
            }
        }
    }

    /// <summary>
    /// Create the pool of chunk objects
    /// </summary>
    public void CreatePool()
    {
        int poolSize = GridSize * GridSize * 4 - 1;
        AllChunks = new TerrainChunk[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            TerrainChunk chunk = GenerateChunk(new Vector3(0, 0, 0));
            chunk.InQueuePool = true;
            ChunkPool.Enqueue(chunk);
            AllChunks[i] = chunk;
        }
    }

    /// <summary>
    /// Generate the levels for the mesh to use
    /// </summary>
    public void GenerateLodLevels()
    {
        MeshData meshData;
        foreach (LodPreset lodpreset in LodPresets)
        {
            if (!lodpreset.Enabled) continue;
            meshData = GenerateMeshData(lodpreset.LodLevel);
            if (!AllMeshData.ContainsKey(lodpreset.LodLevel)) AllMeshData.Add(lodpreset.LodLevel, meshData);
        }
        meshData = GenerateMeshData(FarLodSize);
        if (!AllMeshData.ContainsKey(FarLodSize)) AllMeshData.Add(FarLodSize, meshData);
    }

    /// <summary>
    /// Create a chunk
    /// </summary>
    /// <param name="postion"></param>
    /// <returns></returns>
    public TerrainChunk GenerateChunk(Vector3 position)
    {
        TerrainChunk chunk = Instantiate(ChunkPrefab, position, Quaternion.identity, ChunksParent).GetComponent<TerrainChunk>();
        chunk.gameObject.transform.localScale = ChunkScale;
        chunk.Scale = ChunkScale;
        return chunk;
    }

    /// <summary>
    /// Generate the chunks around a position given the GridSize
    /// </summary>
    private Vector3 _updatedPosition = Vector3.zero;
    private Vector2Int _updatedPositionCache = Vector2Int.zero;
    private Vector3 _actualLocationCache = Vector3.zero;
    public void GenerateChunks(Vector3 position)
    {
        MathUtility.ToTerrainChunkGrid(position, ref _updatedPosition);
        ReQueueOutOfSightChunks(_updatedPosition); // Free up chunks
        //if (ChunkPool.Count == 0) return;
        for (int i = -GridSize; i < GridSize + 1; i++)
        {
            for (int o = -GridSize; o < GridSize + 1; o++)
            {
                _updatedPositionCache.x = Mathf.FloorToInt(_updatedPosition.x + i);
                _updatedPositionCache.y = Mathf.FloorToInt(_updatedPosition.z + o);

                TerrainChunk chunk;
                float lodLevel;
                if (Chunks.ContainsKey(_updatedPositionCache))
                {
                    lodLevel = GetLodLevel(i, o);
                    chunk = Chunks[_updatedPositionCache];
                    if (MathUtility.CompareLodLevel(chunk.LODLevel, lodLevel))
                    {
                        if (lodLevel < FarLodSize)
                        {
                            chunk.IsForcedGeneration = true;
                            lock (ChunksAwaitingMeshApplying) ChunksAwaitingMeshApplying.Enqueue(chunk);
                        }
                        continue;
                    }
                    chunk.LODLevel = lodLevel;
                    chunk.IsForcedGeneration = true;
                    chunk.IsForcedObjectGeneration = true;
                    if (IsMultiThreaded) ChunksAwaitingMeshRecaluation.Enqueue(chunk);
                    else
                    {
                        //GenerateChunkMesh(chunk, lodLevel, true);
                        lock(ChunksAwaitingMeshApplying) ChunksAwaitingMeshApplying.Enqueue(chunk);
                    }
                    continue;
                }
                // Should never update Y axis
                _actualLocationCache.x = _updatedPositionCache.x * ChunkSize;
                _actualLocationCache.z = _updatedPositionCache.y * ChunkSize;

                if (ChunkPool.Count == 0) continue;

                // Move a chunk to a new location and update its mesh
                chunk = ChunkPool.Dequeue();
                //chunk.gameObject.SetActive(true);
                chunk.gameObject.transform.position = _actualLocationCache;
                chunk.Position = _actualLocationCache;
                chunk.InQueuePool = false;
                chunk.ChunkPosition = _updatedPositionCache;
                Chunks.Add(chunk.ChunkPosition, chunk);

                lodLevel = GetLodLevel(i, o);
                chunk.LODLevel = lodLevel;
                if (!MathUtility.CompareLodLevel(chunk.LODLevel, FarLodSize)) chunk.IsForcedObjectGeneration = true;
                chunk.IsForcedGeneration = true;
                if (IsMultiThreaded) ChunksAwaitingMeshRecaluation.Enqueue(chunk);
                else
                {
                    lock (ChunksAwaitingMeshApplying) ChunksAwaitingMeshApplying.Enqueue(chunk);
                }
            }
        }
    }

    /// <summary>
    /// Get the LOD level given the LODLevels
    /// </summary>
    /// <param name="x"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public float GetLodLevel(int x, int z)
    {
        //if (x == 0 && z == 0) return 0.625f;
        foreach(LodPreset lodpreset in LodPresets)
        {
            if (!lodpreset.Enabled) continue;
            if (x < lodpreset.Range && z < lodpreset.Range && x > -lodpreset.Range && z > -lodpreset.Range) return lodpreset.LodLevel;
        }
        return FarLodSize; // Default is one quad - 10
    }

    /// <summary>
    /// Add chunks out-of-sight back into the queue to be used
    /// </summary>
    /// <param name="tilePosition"></param>
    public void ReQueueOutOfSightChunks(Vector3 tilePosition)
    {
        for (int i = AllChunks.Length; i-- > 0;)
        {
            if (AllChunks[i].InQueuePool) continue;
            int xDiff = (int)Mathf.Abs(AllChunks[i].ChunkPosition.x - tilePosition.x);
            int zDiff = (int)Mathf.Abs(AllChunks[i].ChunkPosition.y - tilePosition.z);
            if (xDiff > GridSize || zDiff > GridSize)
            {
                AllChunks[i].InQueuePool = true;
                //AllChunks[i].gameObject.SetActive(false);
                ChunkPool.Enqueue(AllChunks[i]);
                Chunks.Remove(AllChunks[i].ChunkPosition);
            }
        }
    }

    public MeshData GenerateMeshData(float lodLevel)
    {
        float vertsCount = ChunkScale.x + lodLevel;
        int trisCount = (int)(vertsCount / lodLevel);;
 
        List<Vector3> verts = new List<Vector3>(); // Index used in tri list
        List<int> tris = new List<int>(); // Every 3 ints represents a triangle
        List<Vector2> uvs = new List<Vector2>(); // Vertex in 0-1 UV space

        for (float x = 0; x < vertsCount; x += lodLevel)
        {
            for (float z = 0; z < vertsCount; z += lodLevel)
            {
                verts.Add(new Vector3(x, 0, z));
                uvs.Add(new Vector2(x / vertsCount, z / vertsCount));
            }
        }
        // This loop could be moved into above loop
        for (int x = 1; x < trisCount ; x++)
        {
            for (int z = 1; z < trisCount; z++)
            {
                // Unity uses clockwise corner ordering the width of the vertices must be used

                // First triangle
                tris.Add(trisCount * x + z); //Top right
                tris.Add(trisCount * x + (z - 1)); //Bottom right
                tris.Add(trisCount * (x - 1) + (z - 1)); //Bottom left - First triangle

                // Second triangle
                tris.Add(trisCount * (x - 1) + (z - 1)); //Bottom left 
                tris.Add(trisCount * (x - 1) + z); //Top left
                tris.Add(trisCount * x + z); //Top right - Second triangle
            }
        }
        Color[] colours = new Color[verts.Count];

        /*float newSize = vertsCount - lodLevel / 2;
        for (float x = 0; x < newSize; x += lodLevel / 2)
        {
            for (float z = 0; z < newSize; z += lodLevel / 2)
            {
                if (x > 0 && z > 0 && z < newSize - lodLevel / 2 && x < newSize - lodLevel / 2 || verts.Contains(new Vector3(x, 0, z))) continue;
                verts.Add(new Vector3(x, 0, z));
                uvs.Add(new Vector2(x / vertsCount, z / vertsCount));
            }
        }*/

        return new MeshData(verts, tris, uvs, colours);
    }

    /// <summary>
    /// Samples the height map for a given x,z position.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="z"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public static float SampleHeightMapLocal(float x, float z, float y = 0f)
    {
        for (int i = 0; i < instance.Octaves.Length; i++)
        {
            if (!instance.Octaves[i].Enabled) continue;
            float noiseDistance = instance.Octaves[i].NoiseDistance;
            float noiseHeight = instance.Octaves[i].NoiseHeight;

            float xCoord = (x / noiseDistance) + instance.Seed;
            float zCoord = (z / noiseDistance) + instance.Seed;
            y += Mathf.PerlinNoise(xCoord, zCoord) * noiseHeight;
            if (instance.IsBlockTerrain) y = Mathf.Round(y / instance.BlockSize) * instance.BlockSize;
        }
        return y;
    }

    /// <summary>
    /// Samples the height map for a given x,z position.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="z"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public static float SampleHeightMapWorld(float x, float z, float y = 0f)
    {
        for (int i = 0; i < instance.Octaves.Length; i++)
        {
            if (!instance.Octaves[i].Enabled) continue;
            float noiseDistance = instance.Octaves[i].NoiseDistance;
            float noiseHeight = instance.Octaves[i].NoiseHeight;

            float xCoord = (x / ChunkScale.x / noiseDistance) + instance.Seed;
            float zCoord = (z / ChunkScale.x / noiseDistance) + instance.Seed;
            y += Mathf.PerlinNoise(xCoord, zCoord) * noiseHeight;
            if (instance.IsBlockTerrain) y = Mathf.Round(y / instance.BlockSize) * instance.BlockSize;
        }
        return y;
    }

    /// <summary>
    /// Get the neighbouring chunks for all the chunks
    /// </summary>
    public void GenerateAllChunkNeighbours()
    {
        for (int i = AllChunks.Length; i-- > 0;)
        {
            AllChunks[i].GenerateChunkNeighbours();
        }
    }
}

[CustomEditor(typeof(TerrainGeneration))]
public class TerrainGenerationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainGeneration generation = (TerrainGeneration)target;
        if (GUILayout.Button("ReGenerate All Chunk Meshes"))
        {
            foreach(TerrainChunk chunk in TerrainGeneration.Chunks.Values)
            {
                chunk.GenerateChunkMesh();
            }
        }
    }

}
