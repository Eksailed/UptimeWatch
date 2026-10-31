namespace UptimeWatch.Domain;

public sealed class SiteMonitor
{
    private SiteMonitor()
    {
        OwnerId = string.Empty;
        Url = string.Empty;
    }

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; }
    public string Url { get; private set; }
    public int IntervalSeconds { get; private set; }
    public int ExpectedStatusCode { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime NextCheckAt { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public Incident? OpenIncident { get; private set; }

    public static SiteMonitor Create(string ownerId, string url, int intervalSeconds, int expectedStatusCode, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainException(new ErrorInfo(ErrorCode.OwnerRequired, "Owner is required."));
        }

        if (!UrlPolicy.TryValidate(url, out var error))
        {
            throw new DomainException(error);
        }

        if (intervalSeconds < MonitorLimits.MinIntervalSeconds)
        {
            throw new DomainException(new ErrorInfo(
                ErrorCode.IntervalTooShort,
                $"Interval must be at least {MonitorLimits.MinIntervalSeconds} seconds.",
                MonitorLimits.MinIntervalSeconds));
        }

        if (expectedStatusCode is < 100 or > 599)
        {
            throw new DomainException(new ErrorInfo(
                ErrorCode.ExpectedStatusOutOfRange, "Expected status code must be between 100 and 599.", 100, 599));
        }

        return new SiteMonitor
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Url = url,
            IntervalSeconds = intervalSeconds,
            ExpectedStatusCode = expectedStatusCode,
            Enabled = true,
            NextCheckAt = now
        };
    }

    /// <summary>
    /// Applies a check result: updates the failure counter, opens/closes the incident and schedules
    /// the next check. Returns the incident transition so the caller can send a single notification.
    /// </summary>
    public IncidentTransition ApplyResult(CheckResult result, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(result);

        NextCheckAt = now.AddSeconds(IntervalSeconds);

        if (result.Success)
        {
            ConsecutiveFailures = 0;
            if (OpenIncident is null)
            {
                return IncidentTransition.None;
            }

            OpenIncident.Close(now);
            OpenIncident = null;
            return IncidentTransition.Closed;
        }

        ConsecutiveFailures++;

        if (OpenIncident is not null)
        {
            OpenIncident.UpdateError(result.Error);
            return IncidentTransition.None;
        }

        if (ConsecutiveFailures >= MonitorLimits.FailureThreshold)
        {
            OpenIncident = Incident.Open(Id, now, result.Error);
            return IncidentTransition.Opened;
        }

        return IncidentTransition.None;
    }

    public void SetEnabled(bool enabled, DateTime now)
    {
        Enabled = enabled;
        if (enabled)
        {
            NextCheckAt = now;
        }
    }
}
