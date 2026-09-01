#nullable enable
using GameRebellionSdk.Unity;

namespace GameRebellionSdk.Unity.Adapters
{
    /// <summary>
    /// Platform-agnostic interface for GameRebellion SDK adapters.
    /// </summary>
    internal interface IGameRebellionAdapter
    {
        int Initialize(GrConfig config);
        int Shutdown(string? endReason = null);
        /// <summary>0=unknown, 1=granted, 2=denied (GameRebellion.Consent).</summary>
        int SetConsent(int state);
        /// <summary>Opt-in consent policy. Call before Initialize.</summary>
        int SetConsentPolicy(bool requireConsent);
        int SetPaused(bool paused);
        int SetOnline(bool online);
        int Flush();
        int TrackJson(string eventName, string jsonPayload);
        int TrackLog(GrLogEvent log);
        int TrackFeatureUse(GrFeatureUseEvent evt);
        int TrackProgression(GrProgressionEvent evt);
        int TrackLevelUp(GrLevelUpEvent evt);
        int TrackAchievement(GrAchievementEvent evt);
        int TrackFriendInvite(GrFriendInviteEvent evt);
        int TrackGroupJoin(GrGroupJoinEvent evt);
        int TrackChatMessage(GrChatMessageEvent evt);
        int TrackVoiceCallStart(GrVoiceCallStartEvent evt);
        int TrackVoiceCallStop(GrVoiceCallStopEvent evt);
        int TrackLogin(GrLoginEvent evt);
        int TrackLogout(GrLogoutEvent evt);
        int TrackTransaction(GrTransactionEvent evt);
        int TrackCryptoTransaction(GrCryptoTransactionEvent evt);
        int TrackAdView(GrAdViewEvent evt);
        int TrackAdClick(GrAdClickEvent evt);
        int TrackAdError(GrAdErrorEvent evt);
        int TrackAdReward(GrAdRewardEvent evt);
        int GetState();
        string GetLastError();
        string DrainLogs();
        int RecordFrame(double fps);
        int RecordMemory(double memoryMB);
    }
}
