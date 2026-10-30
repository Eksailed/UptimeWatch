using System.Net;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MonitorPagesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Details_page_shows_own_monitor_and_not_found_for_foreign_one()
    {
        var a = await TestUsers.RegisterAsync(_factory);
        var b = await TestUsers.RegisterAsync(_factory);
        var url = $"https://{Guid.NewGuid():N}.example.com";
        Guid monitorId;
        using (var scope = _factory.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<MonitorService>()
                .CreateAsync(a.UserId, url, 60, 200, CancellationToken.None);
            monitorId = created.Value!.Id;
        }

        var own = await GetAsync($"/monitors/{monitorId}", a.Cookie);
        var foreign = await GetAsync($"/monitors/{monitorId}", b.Cookie);

        Assert.Contains(url, own);
        Assert.Contains("Не найдено", foreign);
        Assert.DoesNotContain(url, foreign);
    }

    private async Task<string> GetAsync(string path, string cookie)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
