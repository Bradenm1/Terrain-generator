using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class TerrainChunk : MonoBehaviour {

    public GameObject Plane;
    [HideInInspector]
    public MeshFilter MeshFilter;
    [HideInInspector]
    public Vector2Int ChunkPosition;
    [HideInInspector]
    public MeshCollider MeshCollider;
    [HideInInspector]
    public bool InQueuePool = false;
    public float LODLevel;
    public bool IsForcedGeneration = false;
    public bool IsForcedObjectGeneration = false;
    public TerrainChunk[] ChunkNeighbours = new TerrainChunk[4];
    public Mesh Mesh;
    public Vector3 Position = Vector3.zero;
    public Vector3 Scale = Vector3.zero;

    public List<ChunkContainedObjects> ChunkObjects = new List<ChunkContainedObjects>();

    // Mesh information
    [HideInInspector]
    public Vector3[] _vertices;
    [HideInInspector]
    public int[] _tris;
    [HideInInspector]
    public Vector2[] _uvs;
    [HideInInspector]
    public Color[] _colors;

    public void Awake()
    {
        Plane = gameObject;
        MeshFilter = Plane.GetComponent<MeshFilter>();
        MeshCollider = Plane.GetComponent<MeshCollider>();
        Mesh = new Mesh();
        Mesh.RecalculateBounds();
        Mesh.RecalculateNormals();
        MeshCollider.sharedMesh = Mesh; // Fix the mesh collider issue on mesh update
        MeshFilter.mesh = Mesh;
        for(int i = 0; i < 6; i++)
        {
            ChunkObjects.Add(new ChunkContainedObjects());
        }
    }

    private void OnDestroy()
    {
        if (TerrainGeneration.Chunks.ContainsKey(ChunkPosition)) TerrainGeneration.Chunks.Remove(ChunkPosition);
    }

    public void OnDrawGizmosSelected()
    {
        foreach(var vertice in _vertices)
        {
            Gizmos.color = Color.red;
            Vector3 actualPostiion = new Vector3((vertice.x * Scale.x) + transform.position.x, vertice.y, (vertice.z * Scale.z) + transform.position.z);
            Gizmos.DrawSphere(actualPostiion, 1f);
        }
    }

    private static readonly float _maxColourHeight = 100f;
    private static readonly float _minColourHeight = 0f;
    private static readonly float _widthSizeTolerance = 0.001f;
    /// <summary>
    /// Generate the mesh for a chunk
    /// </summary>
    /// <param name="chunk"></param>
    /// <param name="lodLevel"></param>
    /// <param name="forceReGenerate"></param>
    public void GenerateChunkMesh(float lodLevel = 10, bool forceReGenerate = false)
    {
        if (!forceReGenerate && MathUtility.CompareLodLevel(LODLevel, lodLevel)) return;
        MeshData meshData = TerrainGeneration.AllMeshData[lodLevel];
        Vector3[] vertices = meshData.Vertices;
        Color[] colors = meshData.Colours;

        for (int x = 0; x < vertices.Length; x++)
        {
            float xCoord = (vertices[x].x + Position.x / Scale.x);
            float zCoord = (vertices[x].z + Position.z / Scale.z);
            vertices[x].y = TerrainGeneration.SampleHeightMapLocal(xCoord, zCoord);

            if (vertices[x].y < TerrainGeneration.instance.WaterLayer.position.y)
            {
                colors[x] = TerrainGeneration.instance.UnderWaterColour;
            }
            else
            {
                float colourHeight = Mathf.InverseLerp(_minColourHeight, _maxColourHeight, vertices[x].y);
                colors[x] = TerrainGeneration.instance.TerrainColours.Evaluate(colourHeight);
            }

            // Fix the seams between different size meshes
            if (TerrainGeneration.instance.IsFixingTerrainSeams && lodLevel < TerrainGeneration.instance.FarLodSize)
            {
                float widthSize = (TerrainGeneration.ChunkScale.x + lodLevel) / lodLevel;
                float previousHeight;
                float nextHeight;
                float result;

                if (ChunkNeighbours[3].LODLevel > LODLevel && Math.Abs(x % (widthSize * 2)) < _widthSizeTolerance && x != 0)
                {
                    previousHeight = vertices[x - (int)(widthSize * 2)].y;
                    nextHeight = vertices[x].y;
                    result = (previousHeight + nextHeight) / 2;

                    vertices[x - (int)widthSize].y = result;
                }
                if (ChunkNeighbours[1].LODLevel > LODLevel && x < widthSize && x % 2 == 0 && x != 0)
                {
                    previousHeight = vertices[x - 2].y;
                    nextHeight = vertices[x].y;
                    result = (previousHeight + nextHeight) / 2;

                    vertices[x - 1].y = result;
                }
                if (ChunkNeighbours[0].LODLevel > LODLevel && x > vertices.Length - widthSize && x % 2 == 0 && x != 0)
                {
                    previousHeight = vertices[x - 2].y;
                    nextHeight = vertices[x].y;
                    result = (previousHeight + nextHeight) / 2;

                    vertices[x - 1].y = result;
                }
                // x % (widthSize * 2) == (widthSize * 2) - 1
                if (ChunkNeighbours[2].LODLevel > LODLevel && Math.Abs(x % (widthSize * 2) - ((widthSize * 2) - 1)) < _widthSizeTolerance && x != 0)
                {
                    previousHeight = vertices[x - (int)(widthSize)].y;
                    nextHeight = TerrainGeneration.SampleHeightMapLocal(vertices[x + (int)(widthSize)].x + Position.x / Scale.x, vertices[x + (int)(widthSize)].z + Position.z / Scale.z);
                    result = (nextHeight + previousHeight) / 2;

                    vertices[x].y = result;
                }
            }
        }
        _vertices = vertices;
        _uvs = meshData.Uvs;
        _tris = meshData.Tris;
        _colors = colors;
        //chunk.LODLevel = lodLevel;
    }

    /// <summary>
    /// Update the mesh on a given chunk
    /// </summary>
    /// <param name="chunk"></param>
    public void UpdateChunkMesh()
    {
        //chunk.Mesh = new Mesh();
        Mesh.Clear();
        MeshCollider.enabled = false;
        Mesh.vertices = _vertices;
        Mesh.uv = _uvs;
        Mesh.triangles = _tris;
        Mesh.colors = _colors;
        MeshCollider.enabled = true;
        //chunk.Mesh.RecalculateBounds(); // Assinging triangles already call this
        Mesh.RecalculateNormals();
        //chunk.MeshCollider.sharedMesh = chunk.Mesh; // Fix the mesh collider issue on mesh update
        //chunk.MeshFilter.mesh = chunk.Mesh;
    }

    private static readonly Vector3[] _getneighbours =
    {
        new Vector3(1,0,0),
        new Vector3(-1,0,0),
        new Vector3(0,0,1),
        new Vector3(0,0,-1)
    };
    private Vector2Int _neighbourPositionCache = Vector2Int.zero;
    /// <summary>
    /// Returns the neighbours for a chunk
    /// We do a recursive call incase they have not spawned and when they do they will add themselfs to the neighbours
    /// </summary>
    /// <param name="chunk"></param>
    public void GenerateChunkNeighbours(int nSteps = 1)
    {
        for (int i = 0; i < _getneighbours.Length; i++)
        {
            _neighbourPositionCache.x = (int)_getneighbours[i].x + ChunkPosition.x;
            _neighbourPositionCache.y = (int)_getneighbours[i].z + ChunkPosition.y;
            if (TerrainGeneration.Chunks.ContainsKey(_neighbourPositionCache))
            {
                TerrainChunk neighbourChunk = TerrainGeneration.Chunks[_neighbourPositionCache];
                if (nSteps > 1) neighbourChunk.GenerateChunkNeighbours(nSteps - 1);
                ChunkNeighbours[i] = neighbourChunk;
            }
            else
            {
                ChunkNeighbours[i] = null;
            }
        }
    }
}

[CustomEditor(typeof(TerrainChunk))]
public class TerrainChunkEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainChunk chunk = (TerrainChunk)target;
        if (GUILayout.Button("ReGenerate Chunk Mesh"))
        {
            chunk.GenerateChunkMesh();
        }
    }

}
