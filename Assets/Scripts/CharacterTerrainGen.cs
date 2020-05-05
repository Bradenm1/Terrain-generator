using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterTerrainGen : MonoBehaviour {

    public GameObject SpherePrefab;
    public Transform WaterPlane;
    public Transform Transform;

    private Vector3 _position = Vector3.zero;
    private Vector3 _tilePositionCache = Vector3.zero;
    private Vector3 _currentTilePosition = Vector3.zero;

    /// <summary>
    /// The position of the player
    /// </summary>
    public Vector3 Position
    {
        get => _position;

        set
        {
            MathUtility.ToTerrainChunkGrid(value, ref _tilePositionCache);
            if (_currentTilePosition == _tilePositionCache) return; // Check if the player has moved
            _position = value;
            WaterPlane.position = new Vector3(_position.x, WaterPlane.position.y, _position.z);
            _currentTilePosition = _tilePositionCache;
            if (TerrainGeneration.instance) TerrainGeneration.instance.GenerateChunks(_position); // Generate the chunks around the player
        }
    }

    private void Start()
    {
        Transform = transform;
        WaterPlane.gameObject.SetActive(true);
        StartCoroutine(SetStartingPosition());
    }

    void Update()
    {
        Position = transform.position;
        if (transform.position.y < -50f) ResetHeight(); // In-case the user falls under the map

        if (Input.GetKeyDown(KeyCode.E))
        {
            Vector3 playerPos = Transform.position;
            Vector3 playerDirection = Transform.forward;
            float spawnDistance = 3f;

            Vector3 spawnPos = playerPos + playerDirection * spawnDistance;
            Instantiate(SpherePrefab, spawnPos, Quaternion.identity);
        }
    }

    /// <summary>
    /// Set the starting Y position
    /// </summary>
    /// <returns></returns>
    IEnumerator SetStartingPosition()
    {
        yield return new WaitForEndOfFrame();
        TerrainGeneration.instance.GenerateChunks(_position);
        ResetHeight();
    }

    /// <summary>
    /// Set the Y to be a sample of the height map given an offset to be above the terrain
    /// </summary>
    public void ResetHeight()
    {
        Vector3 startingPosition = new Vector3(Position.x, Position.y, Position.z);
        startingPosition.y = TerrainGeneration.SampleHeightMapWorld(startingPosition.x, startingPosition.z) + 50f;
        gameObject.transform.position = startingPosition;
    }
}
