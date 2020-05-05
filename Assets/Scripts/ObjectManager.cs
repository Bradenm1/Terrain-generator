using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class GameObjectLayer
{
    public GameObject Object;
    public float MinHeight;
    public float MaxHeight;
    public int NumberPerChunk;
    public int MaxPoolSize;
    public bool Enabled;

    [HideInInspector]
    public List<GameObject> AllObjects = new List<GameObject>();
    [HideInInspector]
    public Queue<GameObject> ObjectsWaiting = new Queue<GameObject>();
}

// Used on chunk to tell which is which
public class ChunkContainedObjects
{
    public int Index;
    public List<GameObject> GameObjects = new List<GameObject>();
}

public class ObjectManager : MonoBehaviour {

    public static ObjectManager instance;
    public GameObjectLayer[] gameObjectLayers;
    public Transform ObjectParent;

    private void Awake()
    {
        if (instance == null) instance = this;
        CreateObjects();
    }

    /// <summary>
    /// Creates a pool of objects
    /// </summary>
    public void CreateObjects()
    {
        foreach(GameObjectLayer gameObjectLayer in gameObjectLayers)
        {
            GameObject gameObjectParent = new GameObject();
            gameObjectParent.transform.parent = ObjectParent;
            gameObjectParent.name = String.Format("All_{0}", gameObjectLayer.Object.name);
            for (int i = 0; i < gameObjectLayer.MaxPoolSize; i++)
            {
                GameObject gameObject = Instantiate(gameObjectLayer.Object, gameObjectParent.transform);
                gameObjectLayer.ObjectsWaiting.Enqueue(gameObject);
                gameObjectLayer.AllObjects.Add(gameObject);
            }
        }
    }

    /// <summary>
    /// Places objects randomly on a given chunk
    /// </summary>
    /// <param name="chunk"></param>
    public static void PlaceObjectsAtChunk(TerrainChunk chunk)
    {
        System.Random random = new System.Random(TerrainGeneration.instance.Seed + chunk.ChunkPosition.GetHashCode());
        for (int i = 0; i < instance.gameObjectLayers.Length; i++)
        {
            if (!instance.gameObjectLayers[i].Enabled || chunk.ChunkObjects[i].GameObjects.Count > 0) continue;
            for (int o = 0; o < instance.gameObjectLayers[i].NumberPerChunk; o++)
            {
                float xPos = MathUtility.GetRandomNumber(random,chunk.Position.x, chunk.Position.x + TerrainGeneration.instance.ChunkSize);
                float zPos = MathUtility.GetRandomNumber(random,chunk.Position.z, chunk.Position.z + TerrainGeneration.instance.ChunkSize);
                float yPos = TerrainGeneration.SampleHeightMapWorld(xPos, zPos);
                if (yPos >= instance.gameObjectLayers[i].MinHeight && yPos < instance.gameObjectLayers[i].MaxHeight)
                {
                    Vector3 newPosition = new Vector3(xPos, yPos, zPos);
                    GameObject placedObject = instance.gameObjectLayers[i].ObjectsWaiting.Dequeue();
                    placedObject.transform.position = newPosition;
                    placedObject.SetActive(true);
                    chunk.ChunkObjects[i].GameObjects.Add(placedObject);
                }
            }
        }
    }

    /// <summary>
    /// Removes objects on a given chunk
    /// </summary>
    /// <param name="chunk"></param>
    public static void RemoveObjectsAtChunk(TerrainChunk chunk)
    {
        for (int i = 0; i < chunk.ChunkObjects.Count; i++)
        {
            for (int o = 0; o < chunk.ChunkObjects[i].GameObjects.Count; o++)
            {
                GameObject gameObject = chunk.ChunkObjects[i].GameObjects[o];
                gameObject.SetActive(false);
                instance.gameObjectLayers[i].ObjectsWaiting.Enqueue(gameObject);
            }
            chunk.ChunkObjects[i].GameObjects.Clear();
        }
    }
}
