using System.Net;
using System.Net.Sockets;
using UptimeWatch.Domain;
using UptimeWatch.Infrastructure.Checks;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests;

public class HttpHealthCheckerTests : IAsyncLifetime
{
    private TestSite _site = null!;

    public async Task InitializeAsync() => _site = await TestSite.StartAsync();

    public async Task DisposeAsync() => await _site.DisposeAsync();

    private static HttpHealthChecker Checker(
        TimeSpan? timeout = null,
        bool allowPrivate = true,
        int maxRedirects = 5,
        int maxBytes = 256 * 1024) =>
        new(
            new HealthCheckerOptions
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(10),
                AllowPrivateAddresses = allowPrivate,
                MaxRedirects = maxRedirects,
                MaxResponseBytes = maxBytes
            },
            new FakeDnsResolver());

    private static SiteMonitor Monitor(string url, int expected = 200) =>
        SiteMonitor.Create("owner", url, 60, expected, DateTime.UtcNow);

    [Fact]
    public async Task Check_succeeds_with_status_and_elapsed_ms_when_code_matches()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor(_site.Url("/ok")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Error);
        Assert.InRange(result.ResponseTimeMs, 0, 5000);
    }

    [Fact]
    public async Task Check_fails_when_status_differs_from_expected()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor(_site.Url("/status/503")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(503, result.StatusCode);
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task Check_succeeds_when_expected_status_is_a_non_200_code()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor(_site.Url("/status/404"), 404), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Check_follows_redirect_to_expected_target()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor(_site.Url("/redirect-to-ok")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task Check_does_not_follow_redirect_when_redirect_code_is_expected()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor(_site.Url("/redirect-to-ok"), 302), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(302, result.StatusCode);
    }

    [Fact]
    public async Task Check_resolves_redirect_target_host_through_the_guard()
    {
        var dns = new FakeDnsResolver().Map("other.test", IPAddress.Loopback);
        using var checker = new HttpHealthChecker(new HealthCheckerOptions { AllowPrivateAddresses = true }, dns);
        var target = $"http://other.test:{_site.Port}/ok";

        var result = await checker.CheckAsync(
            Monitor(_site.Url("/redirect-to?url=" + Uri.EscapeDataString(target))),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Contains("site.test", dns.ResolvedHosts);
        Assert.Contains("other.test", dns.ResolvedHosts);
    }

    [Fact]
    public async Task Check_fails_after_too_many_redirects()
    {
        using var checker = Checker(maxRedirects: 3);

        var result = await checker.CheckAsync(Monitor(_site.Url("/loop")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("redirect", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://127.0.0.1/ok")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("ftp://site.test/file")]
    public async Task Check_blocks_redirect_to_non_public_or_non_http_target(string target)
    {
        using var checker = Checker(allowPrivate: true);

        var result = await checker.CheckAsync(
            Monitor(_site.Url("/redirect-to?url=" + Uri.EscapeDataString(target))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_fails_with_timeout_error_when_server_is_slower_than_timeout()
    {
        using var checker = Checker(timeout: TimeSpan.FromMilliseconds(500));

        var result = await checker.CheckAsync(Monitor(_site.Url("/slow")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Timed out", result.Error);
    }

    [Fact]
    public void Constructor_rejects_invalid_options()
    {
        var dns = new FakeDnsResolver();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpHealthChecker(new HealthCheckerOptions { Timeout = TimeSpan.Zero }, dns));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpHealthChecker(new HealthCheckerOptions { MaxRedirects = -1 }, dns));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpHealthChecker(new HealthCheckerOptions { MaxResponseBytes = 0 }, dns));
    }

    [Fact]
    public async Task Check_propagates_cancellation_requested_by_caller()
    {
        using var checker = Checker();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            checker.CheckAsync(Monitor(_site.Url("/slow")), cts.Token));
    }

    [Fact]
    public async Task Check_fails_with_message_on_connection_refused()
    {
        int freePort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        using var checker = Checker();

        var result = await checker.CheckAsync(
            Monitor($"http://{TestSite.HostName}:{freePort}/"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Contains("refused", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_fails_on_unresolvable_host()
    {
        using var checker = Checker();

        var result = await checker.CheckAsync(Monitor("http://nonexistent.invalid/"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("DNS", result.Error);
    }

    [Fact]
    public async Task Check_is_blocked_for_loopback_when_private_addresses_not_allowed()
    {
        using var checker = Checker(allowPrivate: false);

        var result = await checker.CheckAsync(Monitor(_site.Url("/ok")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.StatusCode);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::1")]
    public async Task Check_is_blocked_when_host_resolves_to_non_public_address(string address)
    {
        using var checker = new HttpHealthChecker(new HealthCheckerOptions(), new FakeDnsResolver(IPAddress.Parse(address)));

        var result = await checker.CheckAsync(Monitor(_site.Url("/ok")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_is_blocked_when_any_resolved_address_is_private()
    {
        using var checker = new HttpHealthChecker(
            new HealthCheckerOptions(),
            new FakeDnsResolver(IPAddress.Parse("93.184.216.34"), IPAddress.Loopback));

        var result = await checker.CheckAsync(Monitor(_site.Url("/ok")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Check_reads_at_most_MaxResponseBytes_from_endless_body()
    {
        using var checker = Checker(timeout: TimeSpan.FromSeconds(5), maxBytes: 64 * 1024);

        var result = await checker.CheckAsync(Monitor(_site.Url("/endless")), CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.InRange(result.ResponseTimeMs, 0, 4000);
    }
}
