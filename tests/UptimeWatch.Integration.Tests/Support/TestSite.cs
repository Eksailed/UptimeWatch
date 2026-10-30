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

    private TestSite(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    /// <summary>URL with a non-loopback host name; use together with <see cref="FakeDnsResolver"/>.</summary>
    public string Url(string path) => $"http://{HostName}:{Port}{path}";

    public static async Task<TestSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

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
        return new TestSite(app, new Uri(address).Port);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Maps site.test to 127.0.0.1; every other host does not exist.</summary>
public sealed class FakeDnsResolver(params IPAddress[] addresses) : IDnsResolver
{
    private readonly IPAddress[] _addresses = addresses.Length == 0 ? [IPAddress.Loopback] : addresses;

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        host == TestSite.HostName
            ? Task.FromResult(_addresses)
            : throw new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
}
