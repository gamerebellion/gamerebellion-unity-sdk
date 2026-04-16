using System;

[Serializable]
public class GrLogEvent
{
    public string Type;
    public string Category;
    public string Message;
    public string Description;
    public string ErrorCode;
    public string ErrorDomain;
    public string FeatureName;
    public string StackTrace;
    public string ExceptionType;
    public string MethodName;
    public double? LineNumber;
    public string ThreadId;
    public bool? IsUserAffected;
}
