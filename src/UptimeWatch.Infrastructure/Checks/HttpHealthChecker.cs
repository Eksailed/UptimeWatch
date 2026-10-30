using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Checks;

/// <summary>
/// Performs an HTTP GET against a monitor URL. SSRF protection: every TCP connection (including those
/// made for redirect hops) resolves the host through <see cref="AddressGuard"/> and connects to the
/// validated IP itself, so there is no second DNS resolution that could be rebound to a private address.
/// Redirects are followed manually so each hop is re-validated.
/// </summary>
public sealed class HttpHealthChecker : IHealthChecker, IDisposable
{
    private readonly HealthCheckerOptions _options;
    private readonly IDnsResolver _dns;
    private readonly TimeProvider _time;
    private readonly SocketsHttpHandler _handler;
    private readonly HttpMessageInvoker _invoker;

    public HttpHealthChecker(HealthCheckerOptions options, IDnsResolver dns, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dns);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRedirects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxResponseBytes);

        _options = options;
        _dns = dns;
        _time = time ?? TimeProvider.System;
        _handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.Timeout,
            MaxResponseDrainSize = 0,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectAsync
        };
        _invoker = new HttpMessageInvoker(_handler, disposeHandler: false);
    }

    public void Dispose()
    {
        _invoker.Dispose();
        _handler.Dispose();
    }

    public async Task<CheckResult> CheckAsync(SiteMonitor monitor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var checkedAt = _time.GetUtcNow().UtcDateTime;
        var started = _time.GetTimestamp();
        int? statusCode = null;
        string? error;

        using var timeout = new CancellationTokenSource(_options.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            statusCode = await GetFinalStatusAsync(monitor, linked.Token).ConfigureAwait(false);
            error = statusCode == monitor.ExpectedStatusCode
                ? null
                : $"Unexpected status code {statusCode}, expected {monitor.ExpectedStatusCode}.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            error = $"Timed out after {_options.Timeout.TotalSeconds:0.#} s.";
        }
        catch (CheckFailedException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }

        var elapsedMs = (int)Math.Min((long)_time.GetElapsedTime(started).TotalMilliseconds, int.MaxValue);
        return CheckResult.Create(monitor.Id, checkedAt, error is null, statusCode, elapsedMs, error);
    }

    private async Task<int> GetFinalStatusAsync(SiteMonitor monitor, CancellationToken ct)
    {
        var url = monitor.Url;
        for (var hop = 0; ; hop++)
        {
            if (!UrlPolicy.TryValidate(url, out var urlError))
            {
                throw new CheckFailedException($"Request blocked: {urlError}");
            }

            var uri = new Uri(url);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("UptimeWatch/1.0");

            using var response = await _invoker
                .SendAsync(request, ct)
                .ConfigureAwait(false);
            var status = (int)response.StatusCode;

            // A redirect status the monitor explicitly expects is the final answer, not a hop to follow.
            if (status != monitor.ExpectedStatusCode
                && status is >= 300 and < 400
                && response.Headers.Location is { } location)
            {
                if (hop >= _options.MaxRedirects)
                {
                    throw new CheckFailedException($"Too many redirects (more than {_options.MaxRedirects}).");
                }

                url = (location.IsAbsoluteUri ? location : new Uri(uri, location)).AbsoluteUri;
                continue;
            }

            await DrainBodyAsync(response, ct).ConfigureAwait(false);
            return status;
        }
    }

    private async Task DrainBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[8192];
        var total = 0;
        while (total < _options.MaxResponseBytes)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await AddressGuard
            .ResolveAllowedAsync(endpoint.Host, _dns, _options.AllowPrivateAddresses, ct)
            .ConfigureAwait(false);

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last!;
    }

    private static string Describe(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case BlockedAddressException blocked:
                    return $"Request blocked: {blocked.Message}";
                case SocketException socket:
                    return socket.SocketErrorCode switch
                    {
                        SocketError.ConnectionRefused => "Connection refused.",
                        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                            => "DNS resolution failed: host not found.",
                        SocketError.TimedOut => "Connection timed out.",
                        _ => $"Connection failed ({socket.SocketErrorCode})."
                    };
                case AuthenticationException:
                    return "TLS handshake failed.";
            }
        }

        return ex is HttpRequestException ? "HTTP request failed." : $"Check failed ({ex.GetType().Name}).";
    }

    private sealed class CheckFailedException(string message) : Exception(message);
}
