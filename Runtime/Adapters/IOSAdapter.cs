using System;

using GameRebellionSdk.Unity;

namespace GameRebellionSdk.Unity.Adapters
{
    /// <summary>
    /// iOS platform adapter.
    /// Uses GameRebellionUnityAPI (managed DLL) for Unity IL2CPP compilation.
    /// </summary>
    internal sealed class IOSAdapter : IGameRebellionAdapter
    {
#if UNITY_IOS && !UNITY_EDITOR
        // At iOS build time, the full GameRebellion.Core.dll is available
        // which contains GameRebellionUnityAPI

        public int Initialize(GrConfig config)
        {
            try
            {
                if (config == null)
                {
                    return -2;
                }

                // Set cache directory to Unity's persistent data path (required for iOS)
                var persistentPath = UnityEngine.Application.persistentDataPath;
                global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetCacheDirectory(persistentPath);

                // Convert Unity's GrEnvironment (int) to Core's GrEnvironment
                // Both enums have same values (0=Production, 1=Staging, 2=Development)
                var coreEnv = (global::GameRebellionSdk.Core.Configuration.GrEnvironment)(int)config.Environment;

                global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetDebugLogging(config.IsDebug);

                return global::GameRebellionSdk.Core.GameRebellionUnityAPI.Initialize(
                    config.ApiKey,
                    config.GameVersion,
                    config.BuildNumber,
                    coreEnv,
                    config.BatchSizeBytes,
                    config.BatchMaxEvents,
                    config.FlushIntervalMs,
                    enableCompression: true,
                    autoTrackSession: true
                );
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[IOSAdapter] Initialize failed: {ex.Message}");
                return -1;
            }
        }

        public int Shutdown(string endReason = null)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.Shutdown(endReason);
        }

        public int SetConsent(bool granted)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetConsent(granted);
        }

        public int SetPaused(bool paused)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetPaused(paused);
        }

        public int SetOnline(bool online)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.SetOnline(online);
        }

        public int Flush()
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.Flush();
        }

        public int TrackJson(string eventName, string jsonPayload)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackJson(eventName, jsonPayload);
        }

        public int TrackLog(GrLogEvent log)
        {
            if (log == null || string.IsNullOrEmpty(log.Type))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackLog(
                log.Type,
                log.Category,
                log.Message,
                log.Description,
                log.ErrorCode,
                log.ErrorDomain,
                log.FeatureName,
                log.StackTrace,
                log.ExceptionType,
                log.MethodName,
                log.LineNumber,
                log.ThreadId,
                log.IsUserAffected
            );
        }

        public int TrackFeatureUse(GrFeatureUseEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Type))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackFeatureUse(
                evt.Type, evt.Name, evt.Category, evt.TimeSpent, evt.CompletionStatus);
        }

        public int TrackProgression(GrProgressionEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Type) || string.IsNullOrEmpty(evt.Status))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackProgression(
                evt.Type, evt.Status, evt.Progression01, evt.Progression02, evt.Progression03,
                evt.Difficulty, evt.AttemptNumber, evt.Score, evt.CompletionTime, evt.CompletionPercentage,
                evt.ObjectivesCompleted, evt.ObjectivesTotal, evt.LivesUsed, evt.LivesRemaining,
                evt.HealthRemaining, evt.NewRecord, evt.EnemiesDefeated, evt.ItemsCollected,
                evt.DistanceTraveled, evt.DeathsCount, evt.CurrencyEarned, evt.CurrencyType,
                evt.ExperienceGained, evt.LevelUpTriggered);
        }

        public int TrackLevelUp(GrLevelUpEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackLevelUp(evt.Level);
        }

        public int TrackAchievement(GrAchievementEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Id))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackAchievement(evt.Id);
        }

        public int TrackFriendInvite(GrFriendInviteEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackFriendInvite(evt.InviteMethod, evt.InviteCount);
        }

        public int TrackGroupJoin(GrGroupJoinEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackGroupJoin(
                evt.GroupId, evt.GroupName, evt.GroupSize, evt.GroupType, evt.JoinMethod);
        }

        public int TrackChatMessage(GrChatMessageEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackChatMessage(
                evt.ChatType, evt.MessageLength, evt.MessageType, evt.LanguageDetected);
        }

        public int TrackVoiceCallStart(GrVoiceCallStartEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Type))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackVoiceCallStart(
                evt.Type, evt.CallId, evt.ParticipantCount);
        }

        public int TrackVoiceCallStop(GrVoiceCallStopEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackVoiceCallStop(
                evt.CallId, evt.StopReason);
        }

        public int TrackLogin(GrLoginEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.ExternalUserId))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackLogin(
                evt.ExternalUserId, evt.LoginMethod, evt.LoginProvider, evt.IsFirstLogin, evt.IsReturningUser,
                evt.DaysSinceLastLogin, evt.LoginAttemptCount, evt.PiiConsent, evt.AnalyticsConsent,
                evt.AccountCreatedTime, evt.AccountLevel, evt.AccountStatus, evt.Username, evt.DisplayName,
                evt.FirstName, evt.LastName, evt.Nickname, evt.Email, evt.Phone, evt.Gender, evt.AgeRange,
                evt.BirthYear, evt.Country, evt.Region, evt.City, evt.Language);
        }

        public int TrackLogout(GrLogoutEvent evt)
        {
            if (evt == null)
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackLogout(evt.EndReason, evt.LoginDuration);
        }

        public int TrackTransaction(GrTransactionEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Currency))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackTransaction(
                evt.Amount, evt.Currency, evt.Type, evt.Description, evt.TransactionHash,
                evt.UsdValue, evt.PlatformFee, evt.Status, evt.FailureReason);
        }

        public int TrackCryptoTransaction(GrCryptoTransactionEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Amount) || string.IsNullOrEmpty(evt.Currency))
                return -2;

            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackCryptoTransaction(
                evt.Amount, evt.Currency, evt.Type, evt.Description, evt.TransactionHash,
                evt.BlockNumber, evt.Blockchain, evt.NftContractAddress, evt.NftTokenId,
                evt.NftCollection, evt.NftRarity, evt.NftMetadataUri, evt.GasFee, evt.GasLimit,
                evt.GasPrice, evt.Marketplace, evt.WalletAddress, evt.UsdValue, evt.ExchangeRate,
                evt.PlatformFee, evt.RoyaltyFee, evt.Status, evt.ConfirmationCount, evt.FailureReason,
                evt.GameItemId, evt.PlayerWalletType, evt.IntegrationMethod);
        }

        public int TrackAdView(GrAdViewEvent evt)
        {
            if (evt == null) return -2;
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackAdView(
                evt.Category, evt.AdSdkName, evt.AdPlacement, evt.AdType, evt.AdDuration, evt.AdFirst,
                evt.NetworkId, evt.CampaignId, evt.AdId, evt.NetworkEventId, evt.CreativeWidth, evt.CreativeHeight,
                evt.VideoDuration, evt.LoadTime);
        }

        public int TrackAdClick(GrAdClickEvent evt)
        {
            if (evt == null) return -2;
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackAdClick(
                evt.Category, evt.AdSdkName, evt.AdPlacement, evt.AdType, evt.ClickCoordinateX, evt.ClickCoordinateY,
                evt.TimeToClick, evt.NetworkId, evt.CampaignId, evt.AdId, evt.NetworkEventId);
        }

        public int TrackAdError(GrAdErrorEvent evt)
        {
            if (evt == null) return -2;
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackAdError(
                evt.Category, evt.AdSdkName, evt.AdPlacement, evt.AdType, evt.AdFailShowReason, evt.ErrorCode,
                evt.ErrorMessage, evt.RetryCount, evt.NetworkId, evt.CampaignId, evt.AdId, evt.NetworkEventId,
                evt.AttemptedLoadTime);
        }

        public int TrackAdReward(GrAdRewardEvent evt)
        {
            if (evt == null) return -2;
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.TrackAdReward(
                evt.Category, evt.AdSdkName, evt.AdPlacement, evt.AdType, evt.RewardType, evt.RewardAmount,
                evt.RewardCurrency, evt.CompletionRequired, evt.NetworkId, evt.CampaignId, evt.AdId, evt.NetworkEventId);
        }

        public int GetState()
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.GetState();
        }

        public string GetLastError()
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.GetLastError();
        }

        public string DrainLogs()
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.DrainLogs();
        }

        public int RecordFrame(double fps)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.RecordFrame(fps);
        }

        public int RecordMemory(double memoryMB)
        {
            return global::GameRebellionSdk.Core.GameRebellionUnityAPI.RecordMemory(memoryMB);
        }
#else
        // Stub implementations for Editor and non-iOS platforms
        public int Initialize(GrConfig config) => -1;
        public int Shutdown(string endReason = null) => -1;
        public int SetConsent(bool granted) => -1;
        public int SetPaused(bool paused) => -1;
        public int SetOnline(bool online) => -1;
        public int Flush() => -1;
        public int TrackJson(string eventName, string jsonPayload) => -1;
        public int TrackLog(GrLogEvent log) => -1;
        public int TrackFeatureUse(GrFeatureUseEvent evt) => -1;
        public int TrackProgression(GrProgressionEvent evt) => -1;
        public int TrackLevelUp(GrLevelUpEvent evt) => -1;
        public int TrackAchievement(GrAchievementEvent evt) => -1;
        public int TrackFriendInvite(GrFriendInviteEvent evt) => -1;
        public int TrackGroupJoin(GrGroupJoinEvent evt) => -1;
        public int TrackChatMessage(GrChatMessageEvent evt) => -1;
        public int TrackVoiceCallStart(GrVoiceCallStartEvent evt) => -1;
        public int TrackVoiceCallStop(GrVoiceCallStopEvent evt) => -1;
        public int TrackLogin(GrLoginEvent evt) => -1;
        public int TrackLogout(GrLogoutEvent evt) => -1;
        public int TrackTransaction(GrTransactionEvent evt) => -1;
        public int TrackCryptoTransaction(GrCryptoTransactionEvent evt) => -1;
        public int TrackAdView(GrAdViewEvent evt) => -1;
        public int TrackAdClick(GrAdClickEvent evt) => -1;
        public int TrackAdError(GrAdErrorEvent evt) => -1;
        public int TrackAdReward(GrAdRewardEvent evt) => -1;
        public int GetState() => 0;
        public string GetLastError() => "IOSAdapter not available on this platform";
        public string DrainLogs() => string.Empty;
        public int RecordFrame(double fps) => -1;
        public int RecordMemory(double memoryMB) => -1;
#endif
    }
}
