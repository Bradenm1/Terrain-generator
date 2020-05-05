using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class ThreadsManager : MonoBehaviour {

    public static Thread TerrainMeshUpdaterWorker;
    public static bool IsRunning = true;

    public void Awake()
    {
        if (TerrainMeshUpdaterWorker == null)
        {
            TerrainMeshUpdaterWorker = new Thread(ThreadedWork);
            TerrainMeshUpdaterWorker.Start();
        }
    }

    public void ThreadedWork()
    {
        while (IsRunning)
        {
            if (TerrainGeneration.ChunksAwaitingMeshRecaluation.Count > 0)
            {
                TerrainChunk chunk = TerrainGeneration.ChunksAwaitingMeshRecaluation.Dequeue();
                if (chunk)
                {
                    chunk.GenerateChunkMesh(chunk.LODLevel, chunk.IsForcedGeneration);
                    TerrainGeneration.ChunksAwaitingMeshApplying.Enqueue(chunk);
                }
            }
        }
        Debug.Log("Thread Ended");
    }

    private void OnDestroy()
    {
        Debug.Log("Thread Being Told To End");
        if (TerrainMeshUpdaterWorker != null)
        {
            TerrainMeshUpdaterWorker.Abort();
            TerrainMeshUpdaterWorker = null;
        }
        IsRunning = false;
    }
}
