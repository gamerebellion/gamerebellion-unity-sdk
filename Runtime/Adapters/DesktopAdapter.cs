#nullable enable
using System;
using System.Runtime.InteropServices;
using UnityEngine;

using GameRebellionSdk.Unity;

namespace GameRebellionSdk.Unity.Adapters
{
    /// <summary>
    /// Desktop platform adapter (Windows, macOS, Linux).
    /// Uses DllImport to call native GameRebellion.Core library.
    /// </summary>
    internal sealed class DesktopAdapter : IGameRebellionAdapter
    {
        private const string LibraryName = "GameRebellion.Core";
        private const int BufferSize = 4096;
        private bool _nativeAvailable = true;
        
        [StructLayout(LayoutKind.Sequential)]
        private struct GrConfigNative
        {
            public IntPtr ApiKey;
            public IntPtr GameVersion;
            public IntPtr BuildNumber;
            public int Environment;
            public uint BatchSizeBytes;
            public uint BatchMaxEvents;
            public uint FlushIntervalMs;
            public int EnableCompression;
            public int AutoTrackSession;
        }
        
        [DllImport(LibraryName, EntryPoint = "gr_initialize", CharSet = CharSet.Ansi)]
        private static extern int gr_initialize(ref GrConfigNative config);
        
        [DllImport(LibraryName, EntryPoint = "gr_shutdown", CharSet = CharSet.Ansi)]
        private static extern int gr_shutdown([MarshalAs(UnmanagedType.LPStr)] string endReason);
        
        [DllImport(LibraryName, EntryPoint = "gr_set_consent")]
        private static extern int gr_set_consent(int state);
        
        [DllImport(LibraryName, EntryPoint = "gr_set_paused")]
        private static extern int gr_set_paused(int paused);
        
        [DllImport(LibraryName, EntryPoint = "gr_set_network_online")]
        private static extern int gr_set_network_online(int online);
        
        [DllImport(LibraryName, EntryPoint = "gr_flush")]
        private static extern int gr_flush();
        
        [DllImport(LibraryName, EntryPoint = "gr_track_json", CharSet = CharSet.Ansi)]
        private static extern int gr_track_json(
            [MarshalAs(UnmanagedType.LPStr)] string eventName, 
            [MarshalAs(UnmanagedType.LPStr)] string jsonUtf8);

        [DllImport(LibraryName, EntryPoint = "gr_track_log", CharSet = CharSet.Ansi)]
        private static extern int gr_track_log(
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string category,
            [MarshalAs(UnmanagedType.LPStr)] string message,
            [MarshalAs(UnmanagedType.LPStr)] string description,
            [MarshalAs(UnmanagedType.LPStr)] string errorCode,
            [MarshalAs(UnmanagedType.LPStr)] string errorDomain,
            [MarshalAs(UnmanagedType.LPStr)] string featureName,
            [MarshalAs(UnmanagedType.LPStr)] string stackTrace,
            [MarshalAs(UnmanagedType.LPStr)] string exceptionType,
            [MarshalAs(UnmanagedType.LPStr)] string methodName,
            double lineNumber,
            [MarshalAs(UnmanagedType.LPStr)] string threadId,
            int isUserAffected);

        [DllImport(LibraryName, EntryPoint = "gr_track_feature_use", CharSet = CharSet.Ansi)]
        private static extern int gr_track_feature_use(
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string category,
            double timeSpent,
            [MarshalAs(UnmanagedType.LPStr)] string completionStatus);

        [DllImport(LibraryName, EntryPoint = "gr_track_progression", CharSet = CharSet.Ansi)]
        private static extern int gr_track_progression(
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string status,
            [MarshalAs(UnmanagedType.LPStr)] string progression01,
            [MarshalAs(UnmanagedType.LPStr)] string progression02,
            [MarshalAs(UnmanagedType.LPStr)] string progression03,
            [MarshalAs(UnmanagedType.LPStr)] string difficulty,
            double attemptNumber,
            double score,
            double completionTime,
            double completionPercentage,
            double objectivesCompleted,
            double objectivesTotal,
            double livesUsed,
            double livesRemaining,
            double healthRemaining,
            int newRecord,
            double enemiesDefeated,
            double itemsCollected,
            double distanceTraveled,
            double deathsCount,
            double currencyEarned,
            [MarshalAs(UnmanagedType.LPStr)] string currencyType,
            double experienceGained,
            int levelUpTriggered);

        [DllImport(LibraryName, EntryPoint = "gr_track_level_up")]
        private static extern int gr_track_level_up(double level);

        [DllImport(LibraryName, EntryPoint = "gr_track_achievement", CharSet = CharSet.Ansi)]
        private static extern int gr_track_achievement([MarshalAs(UnmanagedType.LPStr)] string id);

        [DllImport(LibraryName, EntryPoint = "gr_track_friend_invite", CharSet = CharSet.Ansi)]
        private static extern int gr_track_friend_invite(
            [MarshalAs(UnmanagedType.LPStr)] string inviteMethod,
            double inviteCount);

        [DllImport(LibraryName, EntryPoint = "gr_track_group_join", CharSet = CharSet.Ansi)]
        private static extern int gr_track_group_join(
            [MarshalAs(UnmanagedType.LPStr)] string groupId,
            [MarshalAs(UnmanagedType.LPStr)] string groupName,
            double groupSize,
            [MarshalAs(UnmanagedType.LPStr)] string groupType,
            [MarshalAs(UnmanagedType.LPStr)] string joinMethod);

        [DllImport(LibraryName, EntryPoint = "gr_track_chat_message", CharSet = CharSet.Ansi)]
        private static extern int gr_track_chat_message(
            [MarshalAs(UnmanagedType.LPStr)] string chatType,
            double messageLength,
            [MarshalAs(UnmanagedType.LPStr)] string messageType,
            [MarshalAs(UnmanagedType.LPStr)] string languageDetected);

        [DllImport(LibraryName, EntryPoint = "gr_track_voice_call_start", CharSet = CharSet.Ansi)]
        private static extern int gr_track_voice_call_start(
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string callId,
            double participantCount);

        [DllImport(LibraryName, EntryPoint = "gr_track_voice_call_stop", CharSet = CharSet.Ansi)]
        private static extern int gr_track_voice_call_stop(
            [MarshalAs(UnmanagedType.LPStr)] string callId,
            [MarshalAs(UnmanagedType.LPStr)] string stopReason);

        [DllImport(LibraryName, EntryPoint = "gr_track_login", CharSet = CharSet.Ansi)]
        private static extern int gr_track_login(
            [MarshalAs(UnmanagedType.LPStr)] string externalUserId,
            [MarshalAs(UnmanagedType.LPStr)] string loginMethod,
            [MarshalAs(UnmanagedType.LPStr)] string loginProvider,
            int isFirstLogin,
            int isReturningUser,
            double daysSinceLastLogin,
            double loginAttemptCount,
            int piiConsent,
            int analyticsConsent,
            [MarshalAs(UnmanagedType.LPStr)] string accountCreatedTime,
            [MarshalAs(UnmanagedType.LPStr)] string accountLevel,
            [MarshalAs(UnmanagedType.LPStr)] string accountStatus,
            [MarshalAs(UnmanagedType.LPStr)] string username,
            [MarshalAs(UnmanagedType.LPStr)] string displayName,
            [MarshalAs(UnmanagedType.LPStr)] string firstName,
            [MarshalAs(UnmanagedType.LPStr)] string lastName,
            [MarshalAs(UnmanagedType.LPStr)] string nickname,
            [MarshalAs(UnmanagedType.LPStr)] string email,
            [MarshalAs(UnmanagedType.LPStr)] string phone,
            [MarshalAs(UnmanagedType.LPStr)] string gender,
            [MarshalAs(UnmanagedType.LPStr)] string ageRange,
            double birthYear,
            [MarshalAs(UnmanagedType.LPStr)] string country,
            [MarshalAs(UnmanagedType.LPStr)] string region,
            [MarshalAs(UnmanagedType.LPStr)] string city,
            [MarshalAs(UnmanagedType.LPStr)] string language);

        [DllImport(LibraryName, EntryPoint = "gr_track_logout", CharSet = CharSet.Ansi)]
        private static extern int gr_track_logout(
            [MarshalAs(UnmanagedType.LPStr)] string endReason,
            double loginDuration);

        [DllImport(LibraryName, EntryPoint = "gr_track_transaction", CharSet = CharSet.Ansi)]
        private static extern int gr_track_transaction(
            double amount,
            [MarshalAs(UnmanagedType.LPStr)] string currency,
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string description,
            [MarshalAs(UnmanagedType.LPStr)] string transactionHash,
            double usdValue,
            [MarshalAs(UnmanagedType.LPStr)] string platformFee,
            [MarshalAs(UnmanagedType.LPStr)] string status,
            [MarshalAs(UnmanagedType.LPStr)] string failureReason);

        [DllImport(LibraryName, EntryPoint = "gr_track_crypto_transaction", CharSet = CharSet.Ansi)]
        private static extern int gr_track_crypto_transaction(
            [MarshalAs(UnmanagedType.LPStr)] string amount,
            [MarshalAs(UnmanagedType.LPStr)] string currency,
            [MarshalAs(UnmanagedType.LPStr)] string type,
            [MarshalAs(UnmanagedType.LPStr)] string description,
            [MarshalAs(UnmanagedType.LPStr)] string transactionHash,
            [MarshalAs(UnmanagedType.LPStr)] string blockNumber,
            [MarshalAs(UnmanagedType.LPStr)] string blockchain,
            [MarshalAs(UnmanagedType.LPStr)] string nftContractAddress,
            [MarshalAs(UnmanagedType.LPStr)] string nftTokenId,
            [MarshalAs(UnmanagedType.LPStr)] string nftCollection,
            [MarshalAs(UnmanagedType.LPStr)] string nftRarity,
            [MarshalAs(UnmanagedType.LPStr)] string nftMetadataUri,
            [MarshalAs(UnmanagedType.LPStr)] string gasFee,
            [MarshalAs(UnmanagedType.LPStr)] string gasLimit,
            [MarshalAs(UnmanagedType.LPStr)] string gasPrice,
            [MarshalAs(UnmanagedType.LPStr)] string marketplace,
            [MarshalAs(UnmanagedType.LPStr)] string walletAddress,
            double usdValue,
            double exchangeRate,
            [MarshalAs(UnmanagedType.LPStr)] string platformFee,
            [MarshalAs(UnmanagedType.LPStr)] string royaltyFee,
            [MarshalAs(UnmanagedType.LPStr)] string status,
            double confirmationCount,
            [MarshalAs(UnmanagedType.LPStr)] string failureReason,
            [MarshalAs(UnmanagedType.LPStr)] string gameItemId,
            [MarshalAs(UnmanagedType.LPStr)] string playerWalletType,
            [MarshalAs(UnmanagedType.LPStr)] string integrationMethod);

        [DllImport(LibraryName, EntryPoint = "gr_track_ad_view", CharSet = CharSet.Ansi)]
        private static extern int gr_track_ad_view(
            [MarshalAs(UnmanagedType.LPStr)] string category,
            [MarshalAs(UnmanagedType.LPStr)] string adSdkName,
            [MarshalAs(UnmanagedType.LPStr)] string adPlacement,
            [MarshalAs(UnmanagedType.LPStr)] string adType,
            double adDuration,
            int adFirst,
            [MarshalAs(UnmanagedType.LPStr)] string networkId,
            [MarshalAs(UnmanagedType.LPStr)] string campaignId,
            [MarshalAs(UnmanagedType.LPStr)] string adId,
            [MarshalAs(UnmanagedType.LPStr)] string networkEventId,
            double creativeWidth,
            double creativeHeight,
            double videoDuration,
            double loadTime);

        [DllImport(LibraryName, EntryPoint = "gr_track_ad_click", CharSet = CharSet.Ansi)]
        private static extern int gr_track_ad_click(
            [MarshalAs(UnmanagedType.LPStr)] string category,
            [MarshalAs(UnmanagedType.LPStr)] string adSdkName,
            [MarshalAs(UnmanagedType.LPStr)] string adPlacement,
            [MarshalAs(UnmanagedType.LPStr)] string adType,
            double clickCoordinateX,
            double clickCoordinateY,
            double timeToClick,
            [MarshalAs(UnmanagedType.LPStr)] string networkId,
            [MarshalAs(UnmanagedType.LPStr)] string campaignId,
            [MarshalAs(UnmanagedType.LPStr)] string adId,
            [MarshalAs(UnmanagedType.LPStr)] string networkEventId);

        [DllImport(LibraryName, EntryPoint = "gr_track_ad_error", CharSet = CharSet.Ansi)]
        private static extern int gr_track_ad_error(
            [MarshalAs(UnmanagedType.LPStr)] string category,
            [MarshalAs(UnmanagedType.LPStr)] string adSdkName,
            [MarshalAs(UnmanagedType.LPStr)] string adPlacement,
            [MarshalAs(UnmanagedType.LPStr)] string adType,
            [MarshalAs(UnmanagedType.LPStr)] string adFailShowReason,
            [MarshalAs(UnmanagedType.LPStr)] string errorCode,
            [MarshalAs(UnmanagedType.LPStr)] string errorMessage,
            double retryCount,
            [MarshalAs(UnmanagedType.LPStr)] string networkId,
            [MarshalAs(UnmanagedType.LPStr)] string campaignId,
            [MarshalAs(UnmanagedType.LPStr)] string adId,
            [MarshalAs(UnmanagedType.LPStr)] string networkEventId,
            double attemptedLoadTime);

        [DllImport(LibraryName, EntryPoint = "gr_track_ad_reward", CharSet = CharSet.Ansi)]
        private static extern int gr_track_ad_reward(
            [MarshalAs(UnmanagedType.LPStr)] string category,
            [MarshalAs(UnmanagedType.LPStr)] string adSdkName,
            [MarshalAs(UnmanagedType.LPStr)] string adPlacement,
            [MarshalAs(UnmanagedType.LPStr)] string adType,
            [MarshalAs(UnmanagedType.LPStr)] string rewardType,
            double rewardAmount,
            [MarshalAs(UnmanagedType.LPStr)] string rewardCurrency,
            int completionRequired,
            [MarshalAs(UnmanagedType.LPStr)] string networkId,
            [MarshalAs(UnmanagedType.LPStr)] string campaignId,
            [MarshalAs(UnmanagedType.LPStr)] string adId,
            [MarshalAs(UnmanagedType.LPStr)] string networkEventId);
        
        [DllImport(LibraryName, EntryPoint = "gr_get_state")]
        private static extern int gr_get_state();
        
        [DllImport(LibraryName, EntryPoint = "gr_get_last_error")]
        private static extern int gr_get_last_error(IntPtr outBuffer, uint capacity);
        
        [DllImport(LibraryName, EntryPoint = "gr_drain_logs")]
        private static extern int gr_drain_logs(IntPtr outBuffer, uint capacity);
        
        [DllImport(LibraryName, EntryPoint = "gr_record_frame")]
        private static extern int gr_record_frame(double fps);
        
        [DllImport(LibraryName, EntryPoint = "gr_record_memory")]
        private static extern int gr_record_memory(double memoryMB);
        
        public int Initialize(GrConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[DesktopAdapter] Initialize: null config");
                return -2;
            }
            
            IntPtr apiKeyPtr = IntPtr.Zero;
            IntPtr gameVersionPtr = IntPtr.Zero;
            IntPtr buildNumberPtr = IntPtr.Zero;
            
            try
            {
                apiKeyPtr = Marshal.StringToHGlobalAnsi(config.ApiKey);
                gameVersionPtr = Marshal.StringToHGlobalAnsi(config.GameVersion ?? Application.version);
                buildNumberPtr = Marshal.StringToHGlobalAnsi(config.BuildNumber ?? Application.buildGUID);
                
                var nativeConfig = new GrConfigNative
                {
                    ApiKey = apiKeyPtr,
                    GameVersion = gameVersionPtr,
                    BuildNumber = buildNumberPtr,
                    Environment = (int)config.Environment,
                    BatchSizeBytes = config.BatchSizeBytes,
                    BatchMaxEvents = config.BatchMaxEvents,
                    FlushIntervalMs = config.FlushIntervalMs,
                    EnableCompression = config.EnableCompression ? 1 : 0,
                    AutoTrackSession = config.AutoTrackSession ? 1 : 0
                };
                
                return gr_initialize(ref nativeConfig);
            }
            catch (DllNotFoundException ex)
            {
                _nativeAvailable = false;
                Debug.LogError(
                    $"[DesktopAdapter] Native library '{LibraryName}' not found. " +
                    "Ensure the correct native plugin is in Assets/Plugins for your platform:\n" +
                    "  Windows x64: Plugins/x86_64/GameRebellion.Core.dll\n" +
                    "  Linux x64:   Plugins/Linux/x86_64/GameRebellion.Core.so\n" +
                    "  macOS ARM64: Plugins/macOS/GameRebellion.Core.dylib\n" +
                    $"Details: {ex.Message}");
                return -1;
            }
            finally
            {
                if (apiKeyPtr != IntPtr.Zero) Marshal.FreeHGlobal(apiKeyPtr);
                if (gameVersionPtr != IntPtr.Zero) Marshal.FreeHGlobal(gameVersionPtr);
                if (buildNumberPtr != IntPtr.Zero) Marshal.FreeHGlobal(buildNumberPtr);
            }
        }
        
        private int GuardNative()
        {
            if (!_nativeAvailable)
            {
                Debug.LogWarning($"[DesktopAdapter] Native library '{LibraryName}' unavailable; call ignored.");
                return -1;
            }
            return 0;
        }
        
        public int Shutdown(string? endReason = null)
        {
            if (GuardNative() != 0) return -1;
            return gr_shutdown(endReason ?? "normal");
        }
        
        public int SetConsent(bool granted)
        {
            if (GuardNative() != 0) return -1;
            return gr_set_consent(granted ? 1 : 0);
        }
        
        public int SetPaused(bool paused)
        {
            if (GuardNative() != 0) return -1;
            return gr_set_paused(paused ? 1 : 0);
        }
        
        public int SetOnline(bool online)
        {
            if (GuardNative() != 0) return -1;
            return gr_set_network_online(online ? 1 : 0);
        }
        
        public int Flush()
        {
            if (GuardNative() != 0) return -1;
            return gr_flush();
        }
        
        public int TrackJson(string eventName, string jsonPayload)
        {
            if (GuardNative() != 0) return -1;
            return gr_track_json(eventName, jsonPayload);
        }

        public int TrackLog(GrLogEvent log)
        {
            if (GuardNative() != 0) return -1;
            if (log == null || string.IsNullOrEmpty(log.Type))
                return -2;

            double lineNumber = log.LineNumber.HasValue ? log.LineNumber.Value : double.NaN;
            int isUserAffected = log.IsUserAffected.HasValue ? (log.IsUserAffected.Value ? 1 : 0) : -1;

            return gr_track_log(
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
                lineNumber,
                log.ThreadId,
                isUserAffected
            );
        }

        public int TrackFeatureUse(GrFeatureUseEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Type))
                return -2;

            return gr_track_feature_use(
                evt.Type,
                evt.Name,
                evt.Category,
                evt.TimeSpent.HasValue ? evt.TimeSpent.Value : double.NaN,
                evt.CompletionStatus);
        }

        public int TrackProgression(GrProgressionEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Type) || string.IsNullOrEmpty(evt.Status))
                return -2;

            return gr_track_progression(
                evt.Type,
                evt.Status,
                evt.Progression01,
                evt.Progression02,
                evt.Progression03,
                evt.Difficulty,
                evt.AttemptNumber ?? double.NaN,
                evt.Score ?? double.NaN,
                evt.CompletionTime ?? double.NaN,
                evt.CompletionPercentage ?? double.NaN,
                evt.ObjectivesCompleted ?? double.NaN,
                evt.ObjectivesTotal ?? double.NaN,
                evt.LivesUsed ?? double.NaN,
                evt.LivesRemaining ?? double.NaN,
                evt.HealthRemaining ?? double.NaN,
                evt.NewRecord.HasValue ? (evt.NewRecord.Value ? 1 : 0) : -1,
                evt.EnemiesDefeated ?? double.NaN,
                evt.ItemsCollected ?? double.NaN,
                evt.DistanceTraveled ?? double.NaN,
                evt.DeathsCount ?? double.NaN,
                evt.CurrencyEarned ?? double.NaN,
                evt.CurrencyType,
                evt.ExperienceGained ?? double.NaN,
                evt.LevelUpTriggered.HasValue ? (evt.LevelUpTriggered.Value ? 1 : 0) : -1);
        }

        public int TrackLevelUp(GrLevelUpEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_level_up(evt.Level);
        }

        public int TrackAchievement(GrAchievementEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Id))
                return -2;

            return gr_track_achievement(evt.Id);
        }

        public int TrackFriendInvite(GrFriendInviteEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_friend_invite(
                evt.InviteMethod,
                evt.InviteCount ?? double.NaN);
        }

        public int TrackGroupJoin(GrGroupJoinEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_group_join(
                evt.GroupId,
                evt.GroupName,
                evt.GroupSize ?? double.NaN,
                evt.GroupType,
                evt.JoinMethod);
        }

        public int TrackChatMessage(GrChatMessageEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_chat_message(
                evt.ChatType,
                evt.MessageLength ?? double.NaN,
                evt.MessageType,
                evt.LanguageDetected);
        }

        public int TrackVoiceCallStart(GrVoiceCallStartEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Type))
                return -2;

            return gr_track_voice_call_start(
                evt.Type,
                evt.CallId,
                evt.ParticipantCount ?? double.NaN);
        }

        public int TrackVoiceCallStop(GrVoiceCallStopEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_voice_call_stop(
                evt.CallId,
                evt.StopReason);
        }

        public int TrackLogin(GrLoginEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.ExternalUserId))
                return -2;

            return gr_track_login(
                evt.ExternalUserId,
                evt.LoginMethod,
                evt.LoginProvider,
                evt.IsFirstLogin.HasValue ? (evt.IsFirstLogin.Value ? 1 : 0) : -1,
                evt.IsReturningUser.HasValue ? (evt.IsReturningUser.Value ? 1 : 0) : -1,
                evt.DaysSinceLastLogin ?? double.NaN,
                evt.LoginAttemptCount ?? double.NaN,
                evt.PiiConsent.HasValue ? (evt.PiiConsent.Value ? 1 : 0) : -1,
                evt.AnalyticsConsent.HasValue ? (evt.AnalyticsConsent.Value ? 1 : 0) : -1,
                evt.AccountCreatedTime,
                evt.AccountLevel,
                evt.AccountStatus,
                evt.Username,
                evt.DisplayName,
                evt.FirstName,
                evt.LastName,
                evt.Nickname,
                evt.Email,
                evt.Phone,
                evt.Gender,
                evt.AgeRange,
                evt.BirthYear ?? double.NaN,
                evt.Country,
                evt.Region,
                evt.City,
                evt.Language);
        }

        public int TrackLogout(GrLogoutEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null)
                return -2;

            return gr_track_logout(evt.EndReason, evt.LoginDuration ?? double.NaN);
        }

        public int TrackTransaction(GrTransactionEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Currency))
                return -2;

            return gr_track_transaction(
                evt.Amount,
                evt.Currency,
                evt.Type,
                evt.Description,
                evt.TransactionHash,
                evt.UsdValue ?? double.NaN,
                evt.PlatformFee,
                evt.Status,
                evt.FailureReason);
        }

        public int TrackCryptoTransaction(GrCryptoTransactionEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null || string.IsNullOrEmpty(evt.Amount) || string.IsNullOrEmpty(evt.Currency))
                return -2;

            return gr_track_crypto_transaction(
                evt.Amount,
                evt.Currency,
                evt.Type,
                evt.Description,
                evt.TransactionHash,
                evt.BlockNumber,
                evt.Blockchain,
                evt.NftContractAddress,
                evt.NftTokenId,
                evt.NftCollection,
                evt.NftRarity,
                evt.NftMetadataUri,
                evt.GasFee,
                evt.GasLimit,
                evt.GasPrice,
                evt.Marketplace,
                evt.WalletAddress,
                evt.UsdValue ?? double.NaN,
                evt.ExchangeRate ?? double.NaN,
                evt.PlatformFee,
                evt.RoyaltyFee,
                evt.Status,
                evt.ConfirmationCount ?? double.NaN,
                evt.FailureReason,
                evt.GameItemId,
                evt.PlayerWalletType,
                evt.IntegrationMethod);
        }

        public int TrackAdView(GrAdViewEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null) return -2;

            return gr_track_ad_view(
                evt.Category,
                evt.AdSdkName,
                evt.AdPlacement,
                evt.AdType,
                evt.AdDuration ?? double.NaN,
                evt.AdFirst.HasValue ? (evt.AdFirst.Value ? 1 : 0) : -1,
                evt.NetworkId,
                evt.CampaignId,
                evt.AdId,
                evt.NetworkEventId,
                evt.CreativeWidth ?? double.NaN,
                evt.CreativeHeight ?? double.NaN,
                evt.VideoDuration ?? double.NaN,
                evt.LoadTime ?? double.NaN);
        }

        public int TrackAdClick(GrAdClickEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null) return -2;

            return gr_track_ad_click(
                evt.Category,
                evt.AdSdkName,
                evt.AdPlacement,
                evt.AdType,
                evt.ClickCoordinateX ?? double.NaN,
                evt.ClickCoordinateY ?? double.NaN,
                evt.TimeToClick ?? double.NaN,
                evt.NetworkId,
                evt.CampaignId,
                evt.AdId,
                evt.NetworkEventId);
        }

        public int TrackAdError(GrAdErrorEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null) return -2;

            return gr_track_ad_error(
                evt.Category,
                evt.AdSdkName,
                evt.AdPlacement,
                evt.AdType,
                evt.AdFailShowReason,
                evt.ErrorCode,
                evt.ErrorMessage,
                evt.RetryCount ?? double.NaN,
                evt.NetworkId,
                evt.CampaignId,
                evt.AdId,
                evt.NetworkEventId,
                evt.AttemptedLoadTime ?? double.NaN);
        }

        public int TrackAdReward(GrAdRewardEvent evt)
        {
            if (GuardNative() != 0) return -1;
            if (evt == null) return -2;

            return gr_track_ad_reward(
                evt.Category,
                evt.AdSdkName,
                evt.AdPlacement,
                evt.AdType,
                evt.RewardType,
                evt.RewardAmount ?? double.NaN,
                evt.RewardCurrency,
                evt.CompletionRequired.HasValue ? (evt.CompletionRequired.Value ? 1 : 0) : -1,
                evt.NetworkId,
                evt.CampaignId,
                evt.AdId,
                evt.NetworkEventId);
        }
        
        public int GetState()
        {
            if (!_nativeAvailable) return -1;
            return gr_get_state();
        }
        
        public string GetLastError()
        {
            if (!_nativeAvailable) return "Native library not loaded";
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            try
            {
                int result = gr_get_last_error(buffer, BufferSize);
                if (result == 0)
                {
                    return Marshal.PtrToStringAnsi(buffer) ?? "";
                }
                return "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        
        public string DrainLogs()
        {
            if (!_nativeAvailable) return "";
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize * 4);
            try
            {
                int result = gr_drain_logs(buffer, BufferSize * 4);
                if (result == 0)
                {
                    return Marshal.PtrToStringAnsi(buffer) ?? "";
                }
                return "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        
        public int RecordFrame(double fps)
        {
            if (GuardNative() != 0) return -1;
            return gr_record_frame(fps);
        }
        
        public int RecordMemory(double memoryMB)
        {
            if (GuardNative() != 0) return -1;
            return gr_record_memory(memoryMB);
        }
    }
}
