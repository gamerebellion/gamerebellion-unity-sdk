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
        public GrEnvironment Environment { get; set; } = GrEnvironment.Development;
        public uint BatchSizeBytes { get; set; } = 65536;
        public uint BatchMaxEvents { get; set; } = 100;
        public uint FlushIntervalMs { get; set; } = 30000;
        public bool EnableCompression { get; set; } = true;
        public bool AutoTrackSession { get; set; } = true;
        public uint TransportType { get; set; } = 1;
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

    public static bool Initialize(string apiKey)
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
        if (!ValidateConfig(config))
        {
            return false;
        }

        try
        {
            Debug.Log($"[GRC] Initializing SDK: Env={config.Environment}");
            var adapter = GetAdapter();
            int result = adapter.Initialize(config);

            if (result == 0)
            {
                _isInitialized = true;
                Debug.Log("[GRC] SDK initialized successfully");
#if UNITY_EDITOR
                Debug.Log("[GRC] Running in Unity Editor mode - SDK will not send data to server");
#endif
                EnsureMetricsCollector();
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
            GameVersion = string.IsNullOrEmpty(settings.GameVersion) ? (Application.version ?? string.Empty) : settings.GameVersion,
            BuildNumber = string.IsNullOrEmpty(settings.BuildNumber) ? (Application.buildGUID ?? Application.version ?? string.Empty) : settings.BuildNumber,
            Environment = settings.Environment,
            BatchSizeBytes = settings.BatchSizeBytes,
            BatchMaxEvents = settings.BatchMaxEvents,
            FlushIntervalMs = settings.FlushIntervalMs,
            EnableCompression = settings.EnableCompression,
            AutoTrackSession = settings.AutoTrackSession,
            TransportType = settings.TransportType
        };

        return config;
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
        bool granted = consent == Consent.Granted;
        int result = adapter.SetConsent(granted);
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
        if (!_isInitialized)
        {
            Debug.LogError("[GRC] SDK not initialized. Call Initialize() first.");
            return false;
        }

        Debug.Log($"[GRC] TrackLevelCompleted: level={level}, score={score}, difficulty={difficulty}");

        // Convert to JSON payload manually (Unity's JsonUtility doesn't support Dictionary)
        string jsonPayload = $"{{\"level_id\":{level},\"score\":{score},\"difficulty\":\"{difficulty ?? "normal"}\",\"email\":\"{email ?? ""}\"}}";
        
        var adapter = GetAdapter();
        int rc = adapter.TrackJson("level_completed", jsonPayload);
        
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
            return false;
        }

        Debug.Log($"[GRC] TrackEvent: {eventName}");

        var adapter = GetAdapter();
        int rc = adapter.TrackJson(eventName, jsonPayload);
        
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
            return false;
        }

        if (log == null || string.IsNullOrEmpty(log.Type))
        {
            Debug.LogError("[GRC] TrackLog requires a non-empty Type");
            return false;
        }

        var adapter = GetAdapter();
        int rc = adapter.TrackLog(log);
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
            return false;

        return GetAdapter().TrackFeatureUse(evt) == 0;
    }

    public static bool TrackProgression(GrProgressionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Type) || string.IsNullOrEmpty(evt.Status))
            return false;

        return GetAdapter().TrackProgression(evt) == 0;
    }

    public static bool TrackLevelUp(GrLevelUpEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackLevelUp(evt) == 0;
    }

    public static bool TrackAchievement(GrAchievementEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Id))
            return false;

        return GetAdapter().TrackAchievement(evt) == 0;
    }

    public static bool TrackFriendInvite(GrFriendInviteEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackFriendInvite(evt) == 0;
    }

    public static bool TrackGroupJoin(GrGroupJoinEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackGroupJoin(evt) == 0;
    }

    public static bool TrackChatMessage(GrChatMessageEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackChatMessage(evt) == 0;
    }

    public static bool TrackVoiceCallStart(GrVoiceCallStartEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Type))
            return false;

        return GetAdapter().TrackVoiceCallStart(evt) == 0;
    }

    public static bool TrackVoiceCallStop(GrVoiceCallStopEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackVoiceCallStop(evt) == 0;
    }

    public static bool TrackLogin(GrLoginEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.ExternalUserId))
            return false;

        return GetAdapter().TrackLogin(evt) == 0;
    }

    public static bool TrackLogout(GrLogoutEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackLogout(evt) == 0;
    }

    public static bool TrackTransaction(GrTransactionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Currency))
            return false;

        return GetAdapter().TrackTransaction(evt) == 0;
    }

    public static bool TrackCryptoTransaction(GrCryptoTransactionEvent evt)
    {
        if (!EnsureInitialized() || evt == null || string.IsNullOrEmpty(evt.Amount) || string.IsNullOrEmpty(evt.Currency))
            return false;

        return GetAdapter().TrackCryptoTransaction(evt) == 0;
    }

    public static bool TrackAdView(GrAdViewEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackAdView(evt) == 0;
    }

    public static bool TrackAdClick(GrAdClickEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackAdClick(evt) == 0;
    }

    public static bool TrackAdError(GrAdErrorEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackAdError(evt) == 0;
    }

    public static bool TrackAdReward(GrAdRewardEvent evt)
    {
        if (!EnsureInitialized() || evt == null)
            return false;

        return GetAdapter().TrackAdReward(evt) == 0;
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
