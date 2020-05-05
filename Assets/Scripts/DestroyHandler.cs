using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyHandler : MonoBehaviour {

    public static DestroyHandler instance;
    public static Queue<GameObject> Handler = new Queue<GameObject>();

    private static readonly int _maxPerDeletionsPerFrame = 5;
    private int _cachedDeletionAmount = 0;

    void Start()
    {
        if (!instance) instance = this;
    }

    void Update ()
    {
		lock(Handler)
        {
            // The limit for deletion per frame
            while (_cachedDeletionAmount <= _maxPerDeletionsPerFrame)
            {
                if (Handler.Count == 0) return;
                Destroy(Handler.Dequeue());
                _cachedDeletionAmount++;
            }
            _cachedDeletionAmount = 0;
        }
	}
}
