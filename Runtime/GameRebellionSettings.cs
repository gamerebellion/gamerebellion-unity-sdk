using UnityEngine;

namespace GameRebellionSdk.Unity
{
    /// <summary>
    /// Project-wide configuration for the GameRebellion SDK.
    /// The SDK loads this asset from a Resources folder at Initialize() time.
    /// The copy shipped inside the package is read-only; to customize, either
    /// create your own via Assets → Create → GameRebellion → Settings, or use the
    /// GameRebellion.Initialize(apiKey, environment) overload to override the
    /// environment from code.
    /// </summary>
    [CreateAssetMenu(fileName = "GameRebellionSettings", menuName = "GameRebellion/Settings", order = 1)]
    public sealed class GameRebellionSettings : ScriptableObject
    {
        /// <summary>Reported game version. Leave empty to use Application.version (Player Settings → Version).</summary>
        [Header("Core")]
        [Tooltip("Reported game version. Leave empty to use Application.version (Player Settings > Version).")]
        public string GameVersion = "";

        /// <summary>Reported build number: the store's build counter. Leave empty to use the
        /// platform's own -- Android versionCode, iOS CFBundleVersion, the build GUID on desktop.</summary>
        [Tooltip("Reported build number: the store build counter. Leave empty to use the platform's own - Android versionCode, iOS CFBundleVersion, build GUID on desktop.")]
        public string BuildNumber = "";

        /// <summary>
        /// Which GameRebellion backend the SDK talks to.
        /// Production is the default for shipped games; use Staging/Development only for testing.
        /// A GameRebellion.Initialize(apiKey, environment) call overrides this value.
        /// </summary>
        [Tooltip("Backend the SDK talks to. Production (default) for shipped games; Staging/Development for testing. The Initialize(apiKey, environment) overload overrides this value.")]
        public GrEnvironment Environment = GrEnvironment.Production;

        /// <summary>Maximum size of one event batch in bytes before it is sent. Clamped by the SDK to 1 KB – 1 MB.</summary>
        [Header("Batching")]
        [Tooltip("Maximum size of one event batch in bytes before it is sent. Clamped to 1 KB - 1 MB. Larger batches = fewer requests, more data lost on crash.")]
        public uint BatchSizeBytes = 65536;

        /// <summary>Maximum number of events in one batch before it is sent. Clamped by the SDK to 1 – 1000.</summary>
        [Tooltip("Maximum number of events in one batch before it is sent. Clamped to 1 - 1000.")]
        public uint BatchMaxEvents = 100;

        /// <summary>How often (milliseconds) buffered events are flushed to the server even if batch limits are not reached. Clamped by the SDK to 1 s – 5 min.</summary>
        [Tooltip("How often (ms) buffered events are flushed to the server even if batch limits are not reached. Clamped to 1000 - 300000 (1 s - 5 min). Lower = fresher data, more battery/network use.")]
        public uint FlushIntervalMs = 30000;

        /// <summary>
        /// When enabled, the SDK emits verbose debug logs (the same detail you get on Staging/Development)
        /// regardless of the selected environment. Keep disabled in release builds.
        /// </summary>
        [Header("Debug")]
        [Tooltip("Emit verbose SDK debug logs (same detail as Staging/Development) regardless of environment. Keep disabled in release builds.")]
        public bool IsDebug = false;

        [Tooltip(
            "Require explicit consent before anything is transmitted (opt-in model).\n\n" +
            "Off by default, matching how AppsFlyer, Adjust and GameAnalytics behave when a " +
            "game sends no consent signal: events flow until the player explicitly declines " +
            "via SetConsent(Consent.Denied).\n\n" +
            "Turn on for an opt-in model (typically EU/GDPR builds). Events are then withheld " +
            "in memory -- never written to the event cache -- until SetConsent is called: " +
            "Granted replays them so the launch session_start survives the prompt, Denied " +
            "discards them.")]
        public bool RequireConsent = false;
    }
}
