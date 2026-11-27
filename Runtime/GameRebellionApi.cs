public static class GameRebellion
{
    public enum Consent
    {
        Unknown = 0,
        Granted = 1,
        Denied = 2
    }

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

    public enum GrEnvironment
    {
        Production = 0,
        Staging = 1,
        Development = 2
    }

    public static bool IsInitialized => GameRebellionSdk.Unity.GameRebellion.IsInitialized;

    /// <summary>
    /// Initialize the SDK using the environment configured in the GameRebellionSettings asset.
    /// </summary>
    public static bool Initialize(string apiKey)
    {
        return GameRebellionSdk.Unity.GameRebellion.Initialize(apiKey);
    }

    /// <summary>
    /// Initialize the SDK targeting a specific environment, overriding the value
    /// configured in the GameRebellionSettings asset.
    /// </summary>
    public static bool Initialize(string apiKey, GrEnvironment environment)
    {
        return GameRebellionSdk.Unity.GameRebellion.Initialize(
            apiKey,
            (GameRebellionSdk.Unity.GrEnvironment)environment
        );
    }

    /// <summary>
    /// Initialize the SDK targeting a specific environment and debug-logging mode,
    /// overriding both values configured in the GameRebellionSettings asset.
    /// With isDebug enabled the SDK emits verbose debug logs regardless of environment.
    /// </summary>
    public static bool Initialize(string apiKey, GrEnvironment environment, bool isDebug)
    {
        return GameRebellionSdk.Unity.GameRebellion.Initialize(
            apiKey,
            (GameRebellionSdk.Unity.GrEnvironment)environment,
            isDebug
        );
    }

    public static void Shutdown(string endReason = "normal")
    {
        GameRebellionSdk.Unity.GameRebellion.Shutdown(endReason);
    }

    public static SdkState GetState()
    {
        return (SdkState)GameRebellionSdk.Unity.GameRebellion.GetState();
    }

    public static string GetLastError()
    {
        return GameRebellionSdk.Unity.GameRebellion.GetLastError();
    }

    public static void SetConsent(Consent consent)
    {
        GameRebellionSdk.Unity.GameRebellion.SetConsent(
            (GameRebellionSdk.Unity.GameRebellion.Consent)consent
        );
    }

    public static void SetPaused(bool paused)
    {
        GameRebellionSdk.Unity.GameRebellion.SetPaused(paused);
    }

    public static void SetNetworkOnline(bool online)
    {
        GameRebellionSdk.Unity.GameRebellion.SetNetworkOnline(online);
    }

    public static void Flush()
    {
        GameRebellionSdk.Unity.GameRebellion.Flush();
    }

    public static bool TrackLevelCompleted(int level, int score, string difficulty, string email)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackLevelCompleted(level, score, difficulty, email);
    }

    public static bool TrackEvent(string eventName, string jsonPayload)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackEvent(eventName, jsonPayload);
    }

    public static bool TrackLog(GrLogEvent log)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackLog(log);
    }

    public static bool TrackFeatureUse(GrFeatureUseEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackFeatureUse(evt);
    }

    public static bool TrackProgression(GrProgressionEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackProgression(evt);
    }

    public static bool TrackLevelUp(GrLevelUpEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackLevelUp(evt);
    }

    public static bool TrackAchievement(GrAchievementEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackAchievement(evt);
    }

    public static bool TrackFriendInvite(GrFriendInviteEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackFriendInvite(evt);
    }

    public static bool TrackGroupJoin(GrGroupJoinEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackGroupJoin(evt);
    }

    public static bool TrackChatMessage(GrChatMessageEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackChatMessage(evt);
    }

    public static bool TrackVoiceCallStart(GrVoiceCallStartEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackVoiceCallStart(evt);
    }

    public static bool TrackVoiceCallStop(GrVoiceCallStopEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackVoiceCallStop(evt);
    }

    public static bool TrackLogin(GrLoginEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackLogin(evt);
    }

    public static bool TrackLogout(GrLogoutEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackLogout(evt);
    }

    public static bool TrackTransaction(GrTransactionEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackTransaction(evt);
    }

    public static bool TrackCryptoTransaction(GrCryptoTransactionEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackCryptoTransaction(evt);
    }

    public static bool TrackAdView(GrAdViewEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackAdView(evt);
    }

    public static bool TrackAdClick(GrAdClickEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackAdClick(evt);
    }

    public static bool TrackAdError(GrAdErrorEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackAdError(evt);
    }

    public static bool TrackAdReward(GrAdRewardEvent evt)
    {
        return GameRebellionSdk.Unity.GameRebellion.TrackAdReward(evt);
    }

    public static void DrainAndLogSDKLogs()
    {
        GameRebellionSdk.Unity.GameRebellion.DrainAndLogSDKLogs();
    }

    public static bool RecordFrame(double fps)
    {
        return GameRebellionSdk.Unity.GameRebellion.RecordFrame(fps);
    }

    public static bool RecordMemory(double memoryMB)
    {
        return GameRebellionSdk.Unity.GameRebellion.RecordMemory(memoryMB);
    }
}

/// <summary>
/// Automatic in-app purchase reporting.
///
/// The global-namespace facade over <see cref="GameRebellionSdk.Unity.GameRebellionIap"/>,
/// matching how <c>GameRebellion</c> wraps the namespaced SDK entry point.
/// </summary>
public static class GameRebellionIap
{
    /// <summary>
    /// True when this build can observe the platform store: the setting is on,
    /// the platform has an observer, and the OS is new enough to run it.
    /// </summary>
    public static bool IsSupported => GameRebellionSdk.Unity.GameRebellionIap.IsSupported;

    /// <summary>
    /// True once the SDK is reporting purchases by itself.
    ///
    /// The check to branch on in a purchase handler: report the purchase yourself
    /// when this is false, and leave it alone when it is true. That stays correct
    /// on platforms with no observer, on an OS too old to run one, and when a
    /// developer switches auto-tracking off -- without the game testing for
    /// platforms or SDK versions.
    /// </summary>
    public static bool IsObserving => GameRebellionSdk.Unity.GameRebellionIap.IsObserving;

    /// <summary>
    /// Starts observing the store. Idempotent, and called automatically after a
    /// successful Initialize -- only needed if you stopped observation yourself.
    /// </summary>
    public static void StartObservingTransactions()
    {
        GameRebellionSdk.Unity.GameRebellionIap.StartObservingTransactions();
    }

    /// <summary>Stops observing. Your own store code is untouched.</summary>
    public static void StopObservingTransactions()
    {
        GameRebellionSdk.Unity.GameRebellionIap.StopObservingTransactions();
    }
}
