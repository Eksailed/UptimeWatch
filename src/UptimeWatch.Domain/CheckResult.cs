namespace UptimeWatch.Domain;

public sealed class CheckResult
{
    private CheckResult()
    {
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public DateTime CheckedAt { get; private set; }
    public bool Success { get; private set; }
    public int? StatusCode { get; private set; }
    public int ResponseTimeMs { get; private set; }
    public string? Error { get; private set; }

    public static CheckResult Create(
        Guid monitorId,
        DateTime checkedAt,
        bool success,
        int? statusCode,
        int responseTimeMs,
        string? error) =>
        new()
        {
            Id = Guid.NewGuid(),
            MonitorId = monitorId,
            CheckedAt = checkedAt,
            Success = success,
            StatusCode = statusCode,
            ResponseTimeMs = responseTimeMs,
            Error = ErrorText.Truncate(error)
        };
}
