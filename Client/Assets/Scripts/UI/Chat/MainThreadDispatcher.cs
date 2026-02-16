using System;
using System.Collections.Generic;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> actions = new();
    private const int maxPerFrame = 20;

    public static void Enqueue(Action action)
    {
        if (action == null)
            return;

        lock (actions)
            actions.Enqueue(action);
    }

    void Update()
    {
        for (int i = 0; i < maxPerFrame; i++)
        {
            Action action;

            lock (actions)
            {
                if (actions.Count == 0) return;
                action = actions.Dequeue();
            }

            try { action(); }
            catch (Exception e) { Debug.LogError(e); }
        }
    }
}