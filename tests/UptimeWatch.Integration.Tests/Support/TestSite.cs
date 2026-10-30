using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UptimeWatch.Infrastructure.Checks;

namespace UptimeWatch.Integration.Tests.Support;

/// <summary>Kestrel server on a random loopback port with controllable routes.</summary>
public sealed class TestSite : IAsyncDisposable
{
    public const string HostName = "site.test";

    private readonly WebApplication _app;
    private readonly State _state;

    private TestSite(WebApplication app, int port, State state)
    {
        _app = app;
        Port = port;
        _state = state;
    }

    private sealed class State
    {
        public int RequestCount;
        public int ControlledStatus = 200;
    }

    public int Port { get; }

    /// <summary>Total number of HTTP requests the site has received.</summary>
    public int RequestCount => Volatile.Read(ref _state.RequestCount);

    /// <summary>Status code returned by <c>/controlled</c>; can be changed while the site runs.</summary>
    public int ControlledStatus
    {
        get => Volatile.Read(ref _state.ControlledStatus);
        set => Volatile.Write(ref _state.ControlledStatus, value);
    }

    /// <summary>URL with a non-loopback host name; use together with <see cref="FakeDnsResolver"/>.</summary>
    public string Url(string path) => $"http://{HostName}:{Port}{path}";

    public static async Task<TestSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var state = new State();

        app.Use(async (ctx, next) =>
        {
            Interlocked.Increment(ref state.RequestCount);
            await next();
        });
        app.MapGet("/controlled", () => Results.StatusCode(Volatile.Read(ref state.ControlledStatus)));
        app.MapGet("/ok", () => Results.Text("ok"));
        app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
        app.MapGet("/slow", async (HttpContext ctx) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ctx.RequestAborted);
            return Results.Text("slow");
        });
        app.MapGet("/endless", async (HttpContext ctx) =>
        {
            var chunk = new byte[16 * 1024];
            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                await ctx.Response.Body.WriteAsync(chunk, ctx.RequestAborted);
            }
        });
        app.MapGet("/redirect-to-ok", () => Results.Redirect("/ok"));
        app.MapGet("/redirect-to", (string url) => Results.Redirect(url));
        app.MapGet("/loop", () => Results.Redirect("/loop"));

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new TestSite(app, new Uri(address).Port, state);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>
/// Maps site.test to 127.0.0.1 (or the given addresses) plus any extra host mappings; every other host does
/// not exist. Records every host it was asked to resolve.
/// </summary>
public sealed class FakeDnsResolver : IDnsResolver
{
    private readonly Dictionary<string, IPAddress[]> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _calls = new();

    public FakeDnsResolver(params IPAddress[] addresses) =>
        _map[TestSite.HostName] = addresses.Length == 0 ? [IPAddress.Loopback] : addresses;

    public IReadOnlyCollection<string> ResolvedHosts => _calls;

    public FakeDnsResolver Map(string host, params IPAddress[] addresses)
    {
        _map[host] = addresses;
        return this;
    }

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        _calls.Enqueue(host);
        return _map.TryGetValue(host, out var addresses)
            ? Task.FromResult(addresses)
            : throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
    }
}
