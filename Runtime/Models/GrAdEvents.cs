using System;

[Serializable]
public class GrAdViewEvent
{
    public string Category;
    public string AdSdkName;
    public string AdPlacement;
    public string AdType;
    public double? AdDuration;
    public bool? AdFirst;
    public string NetworkId;
    public string CampaignId;
    public string AdId;
    public string NetworkEventId;
    public double? CreativeWidth;
    public double? CreativeHeight;
    public double? VideoDuration;
    public double? LoadTime;
}

[Serializable]
public class GrAdClickEvent
{
    public string Category;
    public string AdSdkName;
    public string AdPlacement;
    public string AdType;
    public double? ClickCoordinateX;
    public double? ClickCoordinateY;
    public double? TimeToClick;
    public string NetworkId;
    public string CampaignId;
    public string AdId;
    public string NetworkEventId;
}

[Serializable]
public class GrAdErrorEvent
{
    public string Category;
    public string AdSdkName;
    public string AdPlacement;
    public string AdType;
    public string AdFailShowReason;
    public string ErrorCode;
    public string ErrorMessage;
    public double? RetryCount;
    public string NetworkId;
    public string CampaignId;
    public string AdId;
    public string NetworkEventId;
    public double? AttemptedLoadTime;
}

[Serializable]
public class GrAdRewardEvent
{
    public string Category;
    public string AdSdkName;
    public string AdPlacement;
    public string AdType;
    public string RewardType;
    public double? RewardAmount;
    public string RewardCurrency;
    public bool? CompletionRequired;
    public string NetworkId;
    public string CampaignId;
    public string AdId;
    public string NetworkEventId;
}
