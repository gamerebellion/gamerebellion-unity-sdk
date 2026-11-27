#nullable enable
using System;
using UnityEngine;
using GameRebellionSdk.Unity.Adapters;

namespace GameRebellionSdk.Unity
{
    public enum GrEnvironment
    {
        Production = 0,
        Staging = 1,
        Development = 2
    }

    internal class GrConfig
    {
        public string ApiKey { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string BuildNumber { get; set; } = "";
        public GrEnvironment Environment { get; set; } = GrEnvironment.Production;
        public uint BatchSizeBytes { get; set; } = 65536;
        public uint BatchMaxEvents { get; set; } = 100;
        public uint FlushIntervalMs { get; set; } = 30000;
        public bool IsDebug { get; set; } = false;
        /// <summary>Opt-in consent policy; see GameRebellionSettings.RequireConsent.</summary>
        public bool RequireConsent { get; set; } = false;
        /// <summary>Background grace period before a session ends; see GameRebellionSettings.</summary>
        public int SessionResumeGraceSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Main entry point for GameRebellion SDK in Unity.
    /// Thin wrapper that delegates to platform-specific adapters via factory pattern.
    /// </summary>
    public static class GameRebellion
    {
    public enum Consent { Unknown = 0, Granted = 1, Denied = 2 }
    public enum SdkState 
    { 
        Uninitialized = 0, 
        Initializing = 1, 
        Ready = 2, 
        Paused = 3, 
        Flushing = 4, 
        ShuttingDown = 5, 
        Error = 6 
    }

    private static bool _isInitialized = false;
    private const string MetricsObjectName = "GameRebellionMetrics";
    
    public static bool IsInitialized => _isInitialized;

    private static IGameRebellionAdapter GetAdapter()
    {
        return GameRebellionAdapterFactory.GetAdapter();
    }

    /// <summary>
    /// Initialize the SDK using the environment configured in the GameRebellionSettings asset.
    /// </summary>
    public static bool Initialize(string apiKey)
    {
        return InitializeInternal(apiKey, null, null);
    }

    /// <summary>
    /// Initialize the SDK targeting a specific environment, overriding the value
    /// configured in the GameRebellionSettings asset.
    /// </summary>
    public static bool Initialize(string apiKey, GrEnvironment environment)
    {
        return InitializeInternal(apiKey, environment, null);
    }

    /// <summary>
    /// Initialize the SDK targeting a specific environment and debug-logging mode,
    /// overriding both values configured in the GameRebellionSettings asset.
    /// With isDebug enabled the SDK emits verbose debug logs regardless of environment.
    /// </summary>
    public static bool Initialize(string apiKey, GrEnvironment environment, bool isDebug)
    {
        return InitializeInternal(apiKey, environment, isDebug);
    }

    private static bool InitializeInternal(string apiKey, GrEnvironment? environmentOverride, bool? isDebugOverride)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[GRC] SDK already initialized");
            return true;
        }

        var settings = LoadSettings();
        if (settings == null)
        {
            Debug.LogError("[GRC] Missing GameRebellionSettings asset. Create it in Resources.");
            return false;
        }

        var config = BuildConfigFromSettings(settings, apiKey);
        if (environmentOverride.HasValue)
        {
            config.Environment = environmentOverride.Value;
        }
        if (isDebugOverride.HasValue)
        {
            config.IsDebug = isDebugOverride.Value;
        }

        if (!ValidateConfig(config))
        {
            return false;
        }

        try
        {
            var envSource = environmentOverride.HasValue ? "override" : "settings";
            Debug.Log($"[GRC] Initializing SDK: Env={config.Environment} ({envSource}), Debug={config.IsDebug}");
            var adapter = GetAdapter();

            // Must precede Initialize: the pipeline reads the policy when it builds the
            // consent gate, so setting it afterwards would leave the first events ungated.
            adapter.SetConsentPolicy(config.RequireConsent);
            if (config.RequireConsent)
            {
                Debug.Log("[GRC] RequireConsent is on -- events are withheld until SetConsent(Consent.Granted)");
            }

            int result = adapter.Initialize(config);

            if (result == 0)
            {
                _isInitialized = true;
                Debug.Log("[GRC] SDK initialized successfully");
#if UNITY_EDITOR
                Debug.Log("[GRC] Running in Unity Editor mode - SDK will not send data to server");
#endif
                EnsureMetricsCollector();
                GameRebellionIap.AutoStart();
                DrainAndLogSDKLogs();
                return true;
            }
            else
            {
                Debug.LogError($"[GRC] SDK initialization failed with code: {result}");
                string error = adapter.GetLastError();
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"[GRC] Error: {error}");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GRC] Initialize exception: {ex.Message}");
            return false;
        }
    }

    private static GrConfig BuildConfigFromSettings(GameRebellionSettings settings, string apiKeyOverride)
    {
        var apiKey = apiKeyOverride;
        var config = new GrConfig
        {
            ApiKey = apiKey ?? string.Empty,
            GameVersion = FirstNonEmpty(settings.GameVersion, Application.version),
            BuildNumber = FirstNonEmpty(settings.BuildNumber, PlatformBuildNumber()),
            Environment = settings.Environment,
            BatchSizeBytes = settings.BatchSizeBytes,
            BatchMaxEvents = settings.BatchMaxEvents,
            FlushIntervalMs = settings.FlushIntervalMs,
            IsDebug = settings.IsDebug,
            RequireConsent = settings.RequireConsent,
            SessionResumeGraceSeconds = settings.SessionResumeGraceSeconds
        };

        return config;
    }

    /// <summary>
    /// build_number is the store's build counter, not an opaque per-build id: Android
    /// versionCode, iOS CFBundleVersion. It has to mean the same thing in both SDKs to
    /// be sortable or groupable -- this used to default to Application.buildGUID, so a
    /// Unity session reported "8c3a4b00d27b4a85bede9175a6d1d8ff" in the column where
    /// an Unreal session reported "1.0.0", and neither could be compared with the other.
    ///
    /// On Android and iOS the answer is left empty on purpose: the SDK's own native
    /// adapter reads versionCode/CFBundleVersion, and a blank config value is what lets
    /// that through (see AndroidDeviceInfoProvider/IOSDeviceInfoProvider). Desktop has
    /// no build counter, so the build GUID stays the fallback there -- it is the only
    /// thing that separates two builds of the same version -- and the Editor, where
    /// buildGUID is empty, reports the version.
    /// </summary>
    private static string PlatformBuildNumber()
    {
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer)
        {
            return string.Empty;
        }

        return FirstNonEmpty(Application.buildGUID, Application.version);
    }

    /// <summary>
    /// First value that is neither null nor blank, else "". Unity hands back empty
    /// strings rather than nulls (Application.buildGUID is "" in the Editor and on
    /// several platforms), so ?? chains silently pick the blank.
    /// </summary>
    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate!;
            }
        }
        return string.Empty;
    }

    private static GameRebellionSettings? LoadSettings()
    {
        return Resources.Load<GameRebellionSettings>("GameRebellionSettings");
    }

    private static bool ValidateConfig(GrConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Debug.LogError("[GRC] ApiKey is required. Pass it to Initialize(apiKey).");
            return false;
        }

        return true;
    }

    public static void Shutdown(string endReason = "normal")
    {
        if (!_isInitialized) return;

        Debug.Log($"[GRC] Shutting down SDK with reason: {endReason}");
        var adapter = GetAdapter();
        adapter.Shutdown(endReason);
        DrainAndLogSDKLogs();
        _isInitialized = false;
        Debug.Log("[GRC] SDK shutdown complete");
    }

    public static SdkState GetState()
    {
        var adapter = GetAdapter();
        int state = adapter.GetState();
        return (SdkState)state;
    }

    public static string GetLastError()
    {
        var adapter = GetAdapter();
        return adapter.GetLastError();
    }

    public static void SetConsent(Consent consent)
    {
        Debug.Log($"[GRC] SetConsent: {consent}");
        var adapter = GetAdapter();
        // Passed through whole. This used to collapse to `consent == Consent.Granted`,
        // which made Denied and Unknown indistinguishable downstream -- so a player who
        // declined looked exactly like one who had not been asked.
        int result = adapter.SetConsent((int)consent);
        if (result != 0)
        {
            Debug.LogWarning($"[GRC] SetConsent failed with code: {result}");
        }
    }

    public static void SetPaused(bool paused)
    {
        var adapter = GetAdapter();
        int result = adapter.SetPaused(paused);
        if (result != 0)
        {
            Debug.LogWarning($"[GRC] SetPaused failed with code: {result}");
        }
    }

    public static void SetNetworkOnline(bool online)
    {
        var adapter = GetAdapter();
        int result = adapter.SetOnline(online);
        if (result != 0)
        {
            Debug.LogWarning($"[GRC] SetNetworkOnline failed with code: {result}");
        }
    }

    public static void Flush()
    {
        var adapter = GetAdapter();
        int result = adapter.Flush();
        if (result != 0)
        {
            Debug.LogWarning($"[GRC] Flush failed with code: {result}");
        }
    }

    public static bool TrackLevelCompleted(int level, int score, string difficulty, string email)
    {
        // The capture says whether an email was passed, never the address.
        var described = $"level_id={level}  score={score}  difficulty={difficulty ?? "normal"}  email={(string.IsNullOrEmpty(email) ? "none" : "set")}";
        if (!_isInitialized)
        {
            Debug.LogError("[GRC] SDK not initialized. Call Initialize() first.");
            return Dropped("level_completed", described, null);
        }

        Debug.Log($"[GRC] TrackLevelCompleted: level={level}, score={score}, difficulty={difficulty}");

        // Convert to JSON payload manually (Unity's JsonUtility doesn't support Dictionary)
        string jsonPayload = $"{{\"level_id\":{level},\"score\":{score},\"difficulty\":\"{difficulty ?? "normal"}\",\"email\":\"{email ?? ""}\"}}";
        
        var adapter = GetAdapter();
        int rc = adapter.TrackJson("level_completed", jsonPayload);
        Sent("level_completed", described, rc);
        
        if (rc == 0)
        {
            Debug.Log("[GRC] Event tracked successfully");
            return true;
        }
        else if (rc == -2)
        {
            Debug.LogWarning("[GRC] Validation failed");
            return false;
        }
        else if (rc == -3)
        {
            Debug.LogError("[GRC] Invalid SDK state");
            return false;
        }
        else
        {
            Debug.LogError($"[GRC] Native error: {rc}");
            return false;
        }
    }

    public static bool TrackEvent(string eventName, string jsonPayload)
    {
        if (!_isInitialized)
        {
            Debug.LogError("[GRC] SDK not initialized. Call Initialize() first.");
            return Dropped(eventName, jsonPayload, null);
        }

        Debug.Log($"[GRC] TrackEvent: {eventName}");

        var adapter = GetAdapter();
        int rc = adapter.TrackJson(eventName, jsonPayload);
        Sent(eventName, jsonPayload, rc);
        
        if (rc == 0)
        {
            return true;
        }
        else
        {
            Debug.LogError($"[GRC] TrackEvent failed with code: {rc}");
            return false;
        }
    }

    public static bool TrackLog(GrLogEvent log)
    {
        if (!_isInitialized)
        {
            Debug.LogError("[GRC] SDK not initialized. Call Initialize() first.");
            return Dropped("log", log, null);
        }

        if (log == null || string.IsNullOrEmpty(log.Type))
        {
            Debug.LogError("[GRC] TrackLog requires a non-empty Type");
            return Dropped("log", log, "Type is required");
        }

        var adapter = GetAdapter();
        int rc = adapter.TrackLog(log);
        Sent("log", log, rc);
        if (rc == 0)
        {
            return true;
        }

        Debug.LogError($"[GRC] TrackLog failed with code: {rc}");
        return false;
    }

    public static bool TrackFeatureUse(GrFeatureUseEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Type))
            return Dropped("feature_use", evt, "Type is required");

        return Sent("feature_use", evt, GetAdapter().TrackFeatureUse(evt));
    }

    public static bool TrackProgression(GrProgressionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Type) || string.IsNullOrEmpty(evt.Status))
            return Dropped("progression", evt, "Type and Status are required");

        return Sent("progression", evt, GetAdapter().TrackProgression(evt));
    }

    public static bool TrackLevelUp(GrLevelUpEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("level_up", evt, null);

        return Sent("level_up", evt, GetAdapter().TrackLevelUp(evt));
    }

    public static bool TrackAchievement(GrAchievementEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Id))
            return Dropped("achievement", evt, "Id is required");

        return Sent("achievement", evt, GetAdapter().TrackAchievement(evt));
    }

    public static bool TrackFriendInvite(GrFriendInviteEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("friend_invite", evt, null);

        return Sent("friend_invite", evt, GetAdapter().TrackFriendInvite(evt));
    }

    public static bool TrackGroupJoin(GrGroupJoinEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("group_join", evt, null);

        return Sent("group_join", evt, GetAdapter().TrackGroupJoin(evt));
    }

    public static bool TrackChatMessage(GrChatMessageEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("chat_message", evt, null);

        return Sent("chat_message", evt, GetAdapter().TrackChatMessage(evt));
    }

    public static bool TrackVoiceCallStart(GrVoiceCallStartEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Type))
            return Dropped("voice_call_start", evt, "Type is required");

        return Sent("voice_call_start", evt, GetAdapter().TrackVoiceCallStart(evt));
    }

    public static bool TrackVoiceCallStop(GrVoiceCallStopEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("voice_call_stop", evt, null);

        return Sent("voice_call_stop", evt, GetAdapter().TrackVoiceCallStop(evt));
    }

    public static bool TrackLogin(GrLoginEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.ExternalUserId))
            return Dropped("login", evt, "ExternalUserId is required");

        return Sent("login", evt, GetAdapter().TrackLogin(evt));
    }

    public static bool TrackLogout(GrLogoutEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("logout", evt, null);

        return Sent("logout", evt, GetAdapter().TrackLogout(evt));
    }

    public static bool TrackTransaction(GrTransactionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Currency))
            return Dropped("transaction", evt, "Currency is required");

        return Sent("transaction", evt, GetAdapter().TrackTransaction(evt));
    }

    public static bool TrackCryptoTransaction(GrCryptoTransactionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Amount) || string.IsNullOrEmpty(evt.Currency))
            return Dropped("crypto_transaction", evt, "Amount and Currency are required");

        return Sent("crypto_transaction", evt, GetAdapter().TrackCryptoTransaction(evt));
    }

    public static bool TrackAdView(GrAdViewEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("ad_view", evt, null);

        return Sent("ad_view", evt, GetAdapter().TrackAdView(evt));
    }

    public static bool TrackAdClick(GrAdClickEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("ad_click", evt, null);

        return Sent("ad_click", evt, GetAdapter().TrackAdClick(evt));
    }

    public static bool TrackAdError(GrAdErrorEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("ad_error", evt, null);

        return Sent("ad_error", evt, GetAdapter().TrackAdError(evt));
    }

    public static bool TrackAdReward(GrAdRewardEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return Dropped("ad_reward", evt, null);

        return Sent("ad_reward", evt, GetAdapter().TrackAdReward(evt));
    }

    public static void DrainAndLogSDKLogs()
    {
        var adapter = GetAdapter();
        string logs = adapter.DrainLogs();
        if (!string.IsNullOrEmpty(logs))
        {
            Debug.Log($"[GRC] SDK Logs:\n{logs}");
        }
    }
    
    public static bool RecordFrame(double fps)
    {
        if (!_isInitialized)
            return false;
        
        var adapter = GetAdapter();
        int result = adapter.RecordFrame(fps);
        return result == 0;
    }
    
    public static bool RecordMemory(double memoryMB)
    {
        if (!_isInitialized)
            return false;
        
        var adapter = GetAdapter();
        int result = adapter.RecordMemory(memoryMB);
        return result == 0;
    }

    // In the Editor, every Track call is recorded for the GameRebellion window's Live
    // events tab, including the ones dropped here (GrEditorEventCapture). In a build
    // these only return the result.
    private static bool Sent(string name, object? payload, int rc)
    {
#if UNITY_EDITOR
        GrEditorEventCapture.Record(name, payload, rc == 0 ? null : $"The SDK rejected it (code {rc}).");
#endif
        return rc == 0;
    }

    private static bool Dropped(string name, object? payload, string? rule)
    {
#if UNITY_EDITOR
        GrEditorEventCapture.Record(name, payload,
            !_isInitialized ? "Dropped: GameRebellion.Initialize hasn't run yet."
            : payload == null ? "Dropped: the event is null."
            : $"Dropped: {rule ?? "it isn't valid"}.");
#endif
        return false;
    }

    private static bool EnsureInitialized()
    {
        if (_isInitialized)
            return true;

        Debug.LogError("[GRC] SDK not initialized. Call Initialize() first.");
        return false;
    }

        private static void EnsureMetricsCollector()
        {
#if UNITY_2023_1_OR_NEWER
            if (UnityEngine.Object.FindFirstObjectByType<GRMetricsCollector>() != null)
                return;
#else
            if (UnityEngine.Object.FindObjectOfType<GRMetricsCollector>() != null)
                return;
#endif

            var obj = new GameObject(MetricsObjectName);
            obj.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(obj);
            obj.AddComponent<GRMetricsCollector>();
        }
    }
}
