using System;

[Serializable]
public class GrFriendInviteEvent
{
    public string InviteMethod;
    public double? InviteCount;
}

[Serializable]
public class GrGroupJoinEvent
{
    public string GroupId;
    public string GroupName;
    public double? GroupSize;
    public string GroupType;
    public string JoinMethod;
}

[Serializable]
public class GrChatMessageEvent
{
    public string ChatType;
    public double? MessageLength;
    public string MessageType;
    public string LanguageDetected;
}

[Serializable]
public class GrVoiceCallStartEvent
{
    public string Type;
    public string CallId;
    public double? ParticipantCount;
}

[Serializable]
public class GrVoiceCallStopEvent
{
    public string CallId;
    public string StopReason;
}

[Serializable]
public class GrLoginEvent
{
    public string ExternalUserId;
    public string LoginMethod;
    public string LoginProvider;
    public bool? IsFirstLogin;
    public bool? IsReturningUser;
    public double? DaysSinceLastLogin;
    public double? LoginAttemptCount;
    public bool? PiiConsent;
    public bool? AnalyticsConsent;
    public string AccountCreatedTime;
    public string AccountLevel;
    public string AccountStatus;
    public string Username;
    public string DisplayName;
    public string FirstName;
    public string LastName;
    public string Nickname;
    public string Email;
    public string Phone;
    public string Gender;
    public string AgeRange;
    public double? BirthYear;
    public string Country;
    public string Region;
    public string City;
    public string Language;
}

[Serializable]
public class GrLogoutEvent
{
    public string EndReason;
    public double? LoginDuration;
}
