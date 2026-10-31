using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Localization;

[Collection(PostgresCollection.Name)]
public sealed class CultureSelectionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RuHero = "Узнайте, что сайт упал, раньше своих пользователей";
    private const string EnHero = "Find out your site is down before your users do";
    private const string RuLogin = "Вход";
    private const string EnLogin = "Log in";

    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Culture_cookie_selects_english_home_page()
    {
        var html = await GetAsync("/", cookie: "c=en|uic=en");

        Assert.Contains(EnHero, html);
        Assert.DoesNotContain(RuHero, html);
        Assert.Contains("<html lang=\"en\"", html);
    }

    [Fact]
    public async Task Culture_cookie_selects_english_login_page()
    {
        var html = await GetAsync("/Account/Login", cookie: "c=en|uic=en");

        Assert.Contains($"<h1>{EnLogin}</h1>", html);
        Assert.DoesNotContain($"<h1>{RuLogin}</h1>", html);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US,en;q=0.9,ru;q=0.5")]
    public async Task Accept_language_selects_english_without_cookie(string acceptLanguage)
    {
        var home = await GetAsync("/", acceptLanguage: acceptLanguage);
        var login = await GetAsync("/Account/Login", acceptLanguage: acceptLanguage);

        Assert.Contains(EnHero, home);
        Assert.Contains("<html lang=\"en\"", home);
        Assert.Contains($"<h1>{EnLogin}</h1>", login);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("de-DE,de;q=0.9")]
    [InlineData("ru")]
    public async Task Russian_is_the_default_when_there_is_no_supported_choice(string? acceptLanguage)
    {
        var home = await GetAsync("/", acceptLanguage: acceptLanguage);
        var login = await GetAsync("/Account/Login", acceptLanguage: acceptLanguage);

        Assert.Contains(RuHero, home);
        Assert.DoesNotContain(EnHero, home);
        Assert.Contains("<html lang=\"ru\"", home);
        Assert.Contains($"<h1>{RuLogin}</h1>", login);
    }

    [Fact]
    public async Task Culture_cookie_wins_over_accept_language()
    {
        var html = await GetAsync("/", cookie: "c=ru|uic=ru", acceptLanguage: "en");

        Assert.Contains(RuHero, html);
        Assert.Contains("<html lang=\"ru\"", html);
    }

    [Fact]
    public async Task Unsupported_culture_cookie_falls_back_to_the_default()
    {
        var html = await GetAsync("/", cookie: "c=de|uic=de");

        Assert.Contains(RuHero, html);
    }

    [Theory]
    [InlineData("c=en|uic=en", "EN")]
    [InlineData("c=ru|uic=ru", "RU")]
    public async Task Switcher_marks_the_active_language(string cookie, string activeLabel)
    {
        var html = await GetAsync("/Account/Login", cookie: cookie);

        var active = Regex.Matches(html, "<a [^>]*aria-current=\"true\"[^>]*>(?<label>[^<]+)</a>");
        var item = Assert.Single(active);
        Assert.Equal(activeLabel, item.Groups["label"].Value);
        Assert.Contains("redirectUri=%2FAccount%2FLogin", html);
        Assert.Contains("data-enhance-nav=\"false\"", html);
    }

    [Fact]
    public async Task Culture_cookie_from_the_endpoint_switches_the_next_page()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var set = await client.GetAsync("/culture/set?culture=en&redirectUri=%2FAccount%2FLogin");
        var cookie = set.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Get, set.Headers.Location);
        request.Headers.Add("Cookie", cookie);
        var page = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains($"<h1>{EnLogin}</h1>", page);
    }

    private async Task<string> GetAsync(string path, string? cookie = null, string? acceptLanguage = null)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $".AspNetCore.Culture={Uri.EscapeDataString(cookie)}");
        }

        if (acceptLanguage is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
