#nullable enable
using UnityEngine;

namespace GameRebellionSdk.Unity.Adapters
{
    /// <summary>
    /// Factory for creating platform-specific GameRebellion SDK adapters.
    /// </summary>
    internal static class GameRebellionAdapterFactory
    {
        private static IGameRebellionAdapter? _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the adapter instance for the current platform.
        /// Thread-safe singleton pattern.
        /// </summary>
        public static IGameRebellionAdapter GetAdapter()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = CreateAdapter();
                    }
                }
            }
            return _instance;
        }

        /// <summary>
        /// Resets the adapter instance (useful for testing).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        private static IGameRebellionAdapter CreateAdapter()
        {
#if UNITY_EDITOR
            Debug.Log("[GameRebellionAdapterFactory] Creating EditorAdapter");
            return new EditorAdapter();
#elif UNITY_ANDROID
            Debug.Log("[GameRebellionAdapterFactory] Creating AndroidAdapter");
            return new AndroidAdapter();
#elif UNITY_IOS
            Debug.Log("[GameRebellionAdapterFactory] Creating IOSAdapter");
            return new IOSAdapter();
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            Debug.Log("[GameRebellionAdapterFactory] Creating DesktopAdapter");
            return new DesktopAdapter();
#else
            // Fallback: runtime detection
            if (System.OperatingSystem.IsAndroid())
            {
                Debug.Log("[GameRebellionAdapterFactory] Runtime detection: Android");
                return new AndroidAdapter();
            }
            if (System.OperatingSystem.IsIOS())
            {
                Debug.Log("[GameRebellionAdapterFactory] Runtime detection: iOS");
                return new IOSAdapter();
            }
            Debug.Log("[GameRebellionAdapterFactory] Fallback: DesktopAdapter");
            return new DesktopAdapter();
#endif
        }
    }
}

