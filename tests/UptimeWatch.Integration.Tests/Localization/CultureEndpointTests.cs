using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Localization;

[Collection(PostgresCollection.Name)]
public sealed class CultureEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    [InlineData("EN")]
    public async Task Supported_culture_sets_the_cookie_and_redirects_back(string culture)
    {
        var response = await GetAsync($"/culture/set?culture={culture}&redirectUri=%2Fmonitors%3Fa%3D1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/monitors?a=1", response.Headers.Location?.OriginalString);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        var code = culture.ToLowerInvariant();
        Assert.StartsWith($".AspNetCore.Culture=c%3D{code}%7Cuic%3D{code};", cookie);
    }

    [Fact]
    public async Task Cookie_lives_one_year_is_lax_and_readable_by_scripts()
    {
        var response = await GetAsync("/culture/set?culture=en&redirectUri=%2F");

        var attributes = Attributes(response);
        Assert.Contains("path=/", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.DoesNotContain("httponly", attributes);
        var expires = attributes.Single(a => a.StartsWith("expires=", StringComparison.Ordinal))["expires=".Length..];
        var left = DateTimeOffset.Parse(expires, CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow;
        Assert.InRange(left.TotalDays, 364, 366);
    }

    [Fact]
    public async Task Secure_flag_follows_the_request_scheme()
    {
        var http = await GetAsync("/culture/set?culture=en", new Uri("http://localhost"));
        var https = await GetAsync("/culture/set?culture=en", new Uri("https://localhost"));

        Assert.DoesNotContain("secure", Attributes(http));
        Assert.Contains("secure", Attributes(https));
    }

    [Theory]
    [InlineData("/culture/set")]
    [InlineData("/culture/set?culture=")]
    [InlineData("/culture/set?culture=de")]
    [InlineData("/culture/set?culture=en-US")]
    [InlineData("/culture/set?culture=ru%7Cevil")]
    [InlineData("/culture/set?culture=..%2F..")]
    public async Task Unsupported_culture_is_rejected_without_a_cookie(string url)
    {
        var response = await GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("///evil.example")]
    [InlineData("http://evil.example")]
    [InlineData("https://evil.example/path")]
    [InlineData("%2F%5Cevil.example")]
    [InlineData("%5Cevil.example")]
    [InlineData("evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("%2Fok%0D%0ASet-Cookie:%20x=1")]
    [InlineData("")]
    public async Task Redirect_targets_that_leave_the_site_fall_back_to_the_home_page(string redirectUri)
    {
        var response = await GetAsync($"/culture/set?culture=en&redirectUri={redirectUri}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Missing_redirect_target_falls_back_to_the_home_page()
    {
        var response = await GetAsync("/culture/set?culture=en");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("%2F", "/")]
    [InlineData("%2FAccount%2FLogin", "/Account/Login")]
    [InlineData("%2Fmonitors%2F5d1c0a2e%3Ftab%3Dchecks%23top", "/monitors/5d1c0a2e?tab=checks#top")]
    public async Task Local_redirect_targets_are_kept(string redirectUri, string expected)
    {
        var response = await GetAsync($"/culture/set?culture=ru&redirectUri={redirectUri}");

        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    private static string[] Attributes(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single().Split(';').Select(a => a.Trim().ToLowerInvariant()).ToArray();

    private async Task<HttpResponseMessage> GetAsync(string url, Uri? baseAddress = null)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = baseAddress ?? new Uri("http://localhost")
        });
        return await client.GetAsync(url);
    }
}
