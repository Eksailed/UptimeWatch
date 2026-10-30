using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Integration.Tests.Support;
using UptimeWatch.Web.Hubs;

namespace UptimeWatch.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MonitorHubTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly List<HubConnection> _connections = [];
    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Update_for_owner_A_is_received_by_A()
    {
        var a = await TestUsers.RegisterAsync(_factory);
        var received = new TaskCompletionSource<MonitorStatusDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = await ConnectAsync(a.Cookie);
        connection.On<MonitorStatusDto>(MonitorHub.MonitorUpdatedEvent, dto => received.TrySetResult(dto));
        var status = Status("https://a.example.com");

        await Publish(a.UserId, status);

        var dto = await received.Task.WaitAsync(Timeout);
        Assert.Equal(status.Id, dto.Id);
        Assert.Equal("https://a.example.com", dto.Url);
        Assert.Equal(200, dto.LastStatusCode);
        Assert.Equal(99.5, dto.Uptime.Last24h);
    }

    [Fact]
    public async Task Update_for_owner_A_is_not_received_by_B()
    {
        var a = await TestUsers.RegisterAsync(_factory);
        var b = await TestUsers.RegisterAsync(_factory);
        var receivedByA = new List<Guid>();
        var receivedByB = new List<Guid>();
        var aGotFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bGotSentinel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forA = Status("https://a.example.com");
        var forB = Status("https://b.example.com");

        var connectionA = await ConnectAsync(a.Cookie);
        connectionA.On<MonitorStatusDto>(MonitorHub.MonitorUpdatedEvent, dto =>
        {
            lock (receivedByA) receivedByA.Add(dto.Id);
            aGotFirst.TrySetResult();
        });
        var connectionB = await ConnectAsync(b.Cookie);
        connectionB.On<MonitorStatusDto>(MonitorHub.MonitorUpdatedEvent, dto =>
        {
            lock (receivedByB) receivedByB.Add(dto.Id);
            if (dto.Id == forB.Id)
            {
                bGotSentinel.TrySetResult();
            }
        });

        await Publish(a.UserId, forA);
        await aGotFirst.Task.WaitAsync(Timeout);

        // Not a sleep: once B has received its own message (sent AFTER A's), B's stream is known to be caught up,
        // so a leaked copy of A's update could only have been delivered before it.
        await Publish(b.UserId, forB);
        await bGotSentinel.Task.WaitAsync(Timeout);

        lock (receivedByB) Assert.Equal([forB.Id], receivedByB);
        lock (receivedByA) Assert.Equal([forA.Id], receivedByA);
    }

    [Fact]
    public async Task Anonymous_connection_to_hub_is_rejected()
    {
        var connection = CreateConnection(cookie: null);
        _connections.Add(connection);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    private static MonitorStatusDto Status(string url) =>
        new(Guid.NewGuid(), url, 60, 200, true, false, 200, 15, DateTime.UtcNow, new UptimeDto(99.5, 99.5, 99.5));

    private Task Publish(string ownerId, MonitorStatusDto status) =>
        _factory.Services.GetRequiredService<IMonitorNotifications>().PublishAsync(ownerId, status, CancellationToken.None);

    private async Task<HubConnection> ConnectAsync(string cookie)
    {
        var connection = CreateConnection(cookie);
        _connections.Add(connection);
        await connection.StartAsync();
        return connection;
    }

    private HubConnection CreateConnection(string? cookie) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, MonitorHub.Route), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // TestServer has no real sockets; long polling goes through the in-memory handler.
                options.Transports = HttpTransportType.LongPolling;
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .Build();
}
