using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MathUtility
{
    /// <summary>
    /// Return a random number between values
    /// </summary>
    /// <param name="random"></param>
    /// <param name="minimum"></param>
    /// <param name="maximum"></param>
    /// <returns></returns>
    public static float GetRandomNumber(System.Random random, float minimum, float maximum)
    {
        return (float)random.NextDouble() * (maximum - minimum) + minimum;
    }

    /// <summary>
    /// Snaps a given Vector3 to the terrain chunk grid
    /// </summary>
    /// <param name="worldPosition"></param>
    /// <param name="newPosition"></param>
    public static void ToTerrainChunkGrid(Vector3 worldPosition, ref Vector3 newPosition)
    {
        newPosition.x = Mathf.FloorToInt((worldPosition.x / TerrainGeneration.instance.ChunkSize));
        newPosition.z = Mathf.FloorToInt((worldPosition.z / TerrainGeneration.instance.ChunkSize));
    }

    private static readonly float _lodEqualTolerance = 0.0001f;
    /// <summary>
    /// Compare if provided lod levels are the same
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool CompareLodLevel(float a, float b)
    {
        return Math.Abs(a - b) < _lodEqualTolerance;
    }
}
