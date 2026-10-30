namespace UptimeWatch.Domain;

public static class MonitorLimits
{
    public const int MinIntervalSeconds = 30;
    public const int FailureThreshold = 3;
    public const int MaxMonitorsPerUser = 20;
    public const int MaxUrlLength = 2048;
}
