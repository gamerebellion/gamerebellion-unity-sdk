#nullable enable

using UnityEngine;
using GameRebellionSdk.Unity;

namespace GameRebellionSdk.Unity.Adapters
{
#if UNITY_EDITOR
    internal sealed class EditorAdapter : IGameRebellionAdapter
    {
        private static bool _hasLoggedEditorMode = false;

        public int Initialize(GrConfig config)
        {
            if (!_hasLoggedEditorMode)
            {
                Debug.LogWarning("[GRC] GameRebellion SDK is running in Unity Editor mode. No data will be sent to the server.");
                _hasLoggedEditorMode = true;
            }
            return 0;
        }

        public int Shutdown(string? endReason = null)
        {
            return 0;
        }

        public int SetConsent(int state)
        {
            return 0;
        }

        public int SetConsentPolicy(bool requireConsent)
        {
            return 0;
        }

        public int SetPaused(bool paused)
        {
            return 0;
        }

        public int SetOnline(bool online)
        {
            return 0;
        }

        public int Flush()
        {
            return 0;
        }

        public int TrackJson(string eventName, string jsonPayload)
        {
            return 0;
        }

        public int TrackLog(GrLogEvent log)
        {
            return 0;
        }

        public int TrackFeatureUse(GrFeatureUseEvent evt)
        {
            return 0;
        }

        public int TrackProgression(GrProgressionEvent evt)
        {
            return 0;
        }

        public int TrackLevelUp(GrLevelUpEvent evt)
        {
            return 0;
        }

        public int TrackAchievement(GrAchievementEvent evt)
        {
            return 0;
        }

        public int TrackFriendInvite(GrFriendInviteEvent evt)
        {
            return 0;
        }

        public int TrackGroupJoin(GrGroupJoinEvent evt)
        {
            return 0;
        }

        public int TrackChatMessage(GrChatMessageEvent evt)
        {
            return 0;
        }

        public int TrackVoiceCallStart(GrVoiceCallStartEvent evt)
        {
            return 0;
        }

        public int TrackVoiceCallStop(GrVoiceCallStopEvent evt)
        {
            return 0;
        }

        public int TrackLogin(GrLoginEvent evt)
        {
            return 0;
        }

        public int TrackLogout(GrLogoutEvent evt)
        {
            return 0;
        }

        public int TrackTransaction(GrTransactionEvent evt)
        {
            return 0;
        }

        public int TrackCryptoTransaction(GrCryptoTransactionEvent evt)
        {
            return 0;
        }

        public int TrackAdView(GrAdViewEvent evt)
        {
            return 0;
        }

        public int TrackAdClick(GrAdClickEvent evt)
        {
            return 0;
        }

        public int TrackAdError(GrAdErrorEvent evt)
        {
            return 0;
        }

        public int TrackAdReward(GrAdRewardEvent evt)
        {
            return 0;
        }

        public int GetState()
        {
            return 2;
        }

        public string GetLastError()
        {
            return string.Empty;
        }

        public string DrainLogs()
        {
            return string.Empty;
        }

        public int RecordFrame(double fps)
        {
            return 0;
        }

        public int RecordMemory(double memoryMB)
        {
            return 0;
        }
    }
#endif
}
