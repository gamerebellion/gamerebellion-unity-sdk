using UnityEngine;
using UnityEngine.UI;

public class GRDemo : MonoBehaviour
{
    [SerializeField]
    private Button _initializeButton;
    [SerializeField]
    private Button _shutdownButton;

    private void OnEnable()
    {
        _initializeButton.onClick.AddListener(Initialize);
        _shutdownButton.onClick.AddListener(Shutdown);
    }

    private void OnDisable()
    {
        _initializeButton.onClick.RemoveListener(Initialize);
        _shutdownButton.onClick.RemoveListener(Shutdown);
    }

    private void Initialize()
    {
        Debug.Log("=== GameRebellion SDK Demo ===");

        bool initSuccess = GameRebellion.Initialize("YOUR_API_KEY");
        gameObject.AddComponent<GRMetricsCollector>();
        gameObject.AddComponent<GRLogDrainer>();
        Debug.Log($"[GRC] Initialize result: {initSuccess}");
    
        if (!initSuccess) 
        {
            Debug.LogError("[GRC] Failed to initialize SDK!");
            string error = GameRebellion.GetLastError();
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[GRC] Error: {error}");
            }
            return;
        }

        var state = GameRebellion.GetState();
        Debug.Log($"[GRC] SDK State: {state}");
    
        GameRebellion.SetConsent(GameRebellion.Consent.Granted);
    
        GameRebellion.TrackLevelCompleted(3, 12345, "hard", "user@example.com");
        GameRebellion.TrackLevelCompleted(4, 20000, "normal", "player@test.com");
        
        string customEventJson = @"{""feature_name"":""inventory"",""action"":""opened"",""items_count"":42}";
        GameRebellion.TrackEvent("custom_event", customEventJson);

        var logEvent = new GrLogEvent
        {
            Type = "error",
            Category = "network",
            Message = "Timeout while fetching profile",
            Description = "GET /profile exceeded 5s timeout",
            ErrorCode = "NET_TIMEOUT",
            FeatureName = "player_profile",
            StackTrace = "DemoStackTrace: line 1",
            IsUserAffected = true
        };
        GameRebellion.TrackLog(logEvent);

        GameRebellion.TrackFeatureUse(new GrFeatureUseEvent
        {
            Type = "ui",
            Name = "inventory",
            Category = "menu",
            TimeSpent = 12.5,
            CompletionStatus = "completed"
        });

        GameRebellion.TrackProgression(new GrProgressionEvent
        {
            Type = "level",
            Status = "complete",
            Progression01 = "world_1",
            Progression02 = "level_3",
            Difficulty = "hard",
            Score = 4500,
            CompletionTime = 92.3,
            NewRecord = true
        });

        GameRebellion.TrackLevelUp(new GrLevelUpEvent
        {
            Level = 12
        });

        GameRebellion.TrackAchievement(new GrAchievementEvent
        {
            Id = "first_win"
        });

        GameRebellion.TrackFriendInvite(new GrFriendInviteEvent
        {
            InviteMethod = "share_link",
            InviteCount = 3
        });

        GameRebellion.TrackGroupJoin(new GrGroupJoinEvent
        {
            GroupId = "clan_1024",
            GroupName = "NightWatch",
            GroupSize = 24,
            GroupType = "clan",
            JoinMethod = "invite"
        });

        GameRebellion.TrackChatMessage(new GrChatMessageEvent
        {
            ChatType = "global",
            MessageLength = 120,
            MessageType = "text",
            LanguageDetected = "en"
        });

        GameRebellion.TrackVoiceCallStart(new GrVoiceCallStartEvent
        {
            Type = "party",
            CallId = "call_abc123",
            ParticipantCount = 4
        });

        GameRebellion.TrackVoiceCallStop(new GrVoiceCallStopEvent
        {
            CallId = "call_abc123",
            StopReason = "user_left"
        });

        GameRebellion.TrackTransaction(new GrTransactionEvent
        {
            Amount = 4.99,
            Currency = "USD",
            Type = "iap",
            Description = "Starter pack",
            TransactionHash = "txn_001",
            Status = "initiated"
        });

        GameRebellion.TrackCryptoTransaction(new GrCryptoTransactionEvent
        {
            Amount = "0.015",
            Currency = "ETH",
            Type = "nft_purchase",
            TransactionHash = "0xabc123",
            Blockchain = "ethereum",
            NftContractAddress = "0xcontract",
            NftTokenId = "987",
            Marketplace = "opensea",
            Status = "pending"
        });

        GameRebellion.TrackAdView(new GrAdViewEvent
        {
            Category = "ads",
            AdSdkName = "unity_ads",
            AdPlacement = "end_of_game",
            AdType = "rewarded",
            AdDuration = 30,
            AdFirst = true,
            NetworkEventId = "ad_evt_001"
        });

        GameRebellion.TrackAdClick(new GrAdClickEvent
        {
            Category = "ads",
            AdSdkName = "unity_ads",
            AdPlacement = "end_of_game",
            AdType = "rewarded",
            ClickCoordinateX = 123,
            ClickCoordinateY = 456,
            TimeToClick = 2.4
        });

        GameRebellion.TrackAdError(new GrAdErrorEvent
        {
            Category = "ads",
            AdSdkName = "unity_ads",
            AdPlacement = "shop",
            AdType = "video",
            AdFailShowReason = "no_fill",
            ErrorCode = "NO_FILL",
            ErrorMessage = "No ad available",
            RetryCount = 1,
            AttemptedLoadTime = 1.2
        });

        GameRebellion.TrackAdReward(new GrAdRewardEvent
        {
            Category = "ads",
            AdSdkName = "unity_ads",
            AdPlacement = "end_of_game",
            AdType = "rewarded",
            RewardType = "currency",
            RewardAmount = 50,
            RewardCurrency = "coins",
            CompletionRequired = true
        });
        
        Debug.Log("[GRC] Demo initialization complete");
    }

    private void Shutdown()
    {
        Debug.Log("[GRC] Quitting SDK");
        GameRebellion.Shutdown();
    }
}
