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

        /// <summary>
        /// Opt in to automatic in-app purchase reporting. Read by editor code only --
        /// see GRIapDefineSync. Changing it rewrites a scripting define, so it takes
        /// effect on the next build, not at runtime.
        /// </summary>
        /// <summary>
        /// How long the app may sit in the background before the session ends.
        /// 0 restores the old behaviour of ending it the instant the app is
        /// backgrounded.
        /// </summary>
        [Tooltip(
            "How long (seconds) the app may stay in the background before the " +
            "session is considered over.\n\n" +
            "30 by default. Without it, every trip out of the app costs a session: " +
            "a purchase sheet, a rewarded ad, a screenshot or a glance at a " +
            "notification each end one session and start another, inflating your " +
            "session count and dragging average duration toward zero.\n\n" +
            "Set to 0 to end the session the moment the app is backgrounded.")]
        [Range(0, 1800)]
        public int SessionResumeGraceSeconds = 30;

        [Header("Monetization")]
        [Tooltip(
            "Automatically report in-app purchases to GameRebellion.\n\n" +
            "Off by default: purchases are reported only when the game calls " +
            "TrackTransaction itself.\n\n" +
            "When enabled, the SDK observes the platform store directly - StoreKit " +
            "on iOS/macOS - and reports purchases with no per-purchase game code. " +
            "Requires iOS 15 or newer. Changing this requires a rebuild.")]
        public bool AutoTrackPurchases = false;
    }
}
