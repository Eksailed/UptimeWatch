namespace UptimeWatch.Infrastructure.Checks;

public sealed class HealthCheckerOptions
{
    /// <summary>Total time budget for one check, including redirects and reading the body.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public int MaxResponseBytes { get; init; } = 256 * 1024;

    public int MaxRedirects { get; init; } = 5;

    /// <summary>Disables the SSRF guard. Exists only for tests; never set it in production code.</summary>
    public bool AllowPrivateAddresses { get; init; }
}
