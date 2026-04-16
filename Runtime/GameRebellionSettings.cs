using UnityEngine;

namespace GameRebellionSdk.Unity
{
    [CreateAssetMenu(fileName = "GameRebellionSettings", menuName = "GameRebellion/Settings", order = 1)]
    public sealed class GameRebellionSettings : ScriptableObject
    {
        [Header("Core")]
        public string GameVersion = "";
        public string BuildNumber = "";
        public GrEnvironment Environment = GrEnvironment.Development;

        [Header("Batching")]
        public uint BatchSizeBytes = 65536;
        public uint BatchMaxEvents = 100;
        public uint FlushIntervalMs = 30000;

        [Header("Behavior")]
        public bool EnableCompression = true;
        public bool AutoTrackSession = true;
        public uint TransportType = 1;
    }
}
