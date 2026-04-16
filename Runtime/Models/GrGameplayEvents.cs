using System;

[Serializable]
public class GrFeatureUseEvent
{
    public string Type;
    public string Name;
    public string Category;
    public double? TimeSpent;
    public string CompletionStatus;
}

[Serializable]
public class GrProgressionEvent
{
    public string Type;
    public string Status;
    public string Progression01;
    public string Progression02;
    public string Progression03;
    public string Difficulty;
    public double? AttemptNumber;
    public double? Score;
    public double? CompletionTime;
    public double? CompletionPercentage;
    public double? ObjectivesCompleted;
    public double? ObjectivesTotal;
    public double? LivesUsed;
    public double? LivesRemaining;
    public double? HealthRemaining;
    public bool? NewRecord;
    public double? EnemiesDefeated;
    public double? ItemsCollected;
    public double? DistanceTraveled;
    public double? DeathsCount;
    public double? CurrencyEarned;
    public string CurrencyType;
    public double? ExperienceGained;
    public bool? LevelUpTriggered;
}

[Serializable]
public class GrLevelUpEvent
{
    public double Level;
}

[Serializable]
public class GrAchievementEvent
{
    public string Id;
}
