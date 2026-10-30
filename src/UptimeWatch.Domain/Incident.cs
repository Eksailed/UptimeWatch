namespace UptimeWatch.Domain;

public sealed class Incident
{
    private Incident()
    {
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public string? LastError { get; private set; }

    public TimeSpan? Duration => EndedAt is { } ended ? ended - StartedAt : null;

    internal static Incident Open(Guid monitorId, DateTime startedAt, string? error) =>
        new()
        {
            Id = Guid.NewGuid(),
            MonitorId = monitorId,
            StartedAt = startedAt,
            LastError = ErrorText.Truncate(error)
        };

    internal void UpdateError(string? error) => LastError = ErrorText.Truncate(error);

    internal void Close(DateTime endedAt) => EndedAt = endedAt;
}
