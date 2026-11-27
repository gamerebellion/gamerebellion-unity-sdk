using System;
using System.Collections.Concurrent;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Runs work posted from the bridge's socket threads on the Editor's main
    /// thread, where AssetDatabase, CompilationPipeline and play mode live.
    /// Drained from EditorApplication.update, which keeps ticking (slower) while
    /// Unity is in the background, the normal case while the developer is in the IDE.
    /// </summary>
    internal static class GRMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static bool _hooked;

        /// <summary>Hooks the drain. Safe to call more than once; needed again after every domain reload.</summary>
        public static void Init()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            EditorApplication.update += Drain;
        }

        public static void Post(Action action)
        {
            Queue.Enqueue(action);
        }

        private static void Drain()
        {
            while (Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
