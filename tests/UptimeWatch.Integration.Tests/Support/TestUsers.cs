using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Infrastructure.Identity;

namespace UptimeWatch.Integration.Tests.Support;

/// <summary>Creates users and signs them in through the real Identity login form.</summary>
public static class TestUsers
{
    private const string Password = "Test-Passw0rd!";

    /// <summary>Creates a user and signs in through the real Identity login form, returning the auth cookie header.</summary>
    public static async Task<(string UserId, string Cookie)> RegisterAsync(WebAppFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Code)));
            userId = user.Id;
        }

        return (userId, await LoginAsync(factory, email));
    }

    private static async Task<string> LoginAsync(WebAppFactory factory, string email)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var cookies = new Dictionary<string, string>();

        var page = await client.GetAsync("/Account/Login");
        page.EnsureSuccessStatusCode();
        Collect(page, cookies);
        var html = await page.Content.ReadAsStringAsync();

        using var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = HiddenValue(html, "__RequestVerificationToken"),
                ["_handler"] = HiddenValue(html, "_handler"),
                ["Input.Email"] = email,
                ["Input.Password"] = Password,
                ["Input.RememberMe"] = "false"
            })
        };
        post.Headers.Add("Cookie", Header(cookies));
        var response = await client.SendAsync(post);
        Collect(response, cookies);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(cookies.Keys, name => name.Contains("Identity.Application"));
        return Header(cookies);
    }

    private static string HiddenValue(string html, string name)
    {
        var escaped = Regex.Escape(name);
        var match = Regex.Match(html, $"<input[^>]*name=\"{escaped}\"[^>]*value=\"([^\"]*)\"");
        if (!match.Success)
        {
            match = Regex.Match(html, $"<input[^>]*value=\"([^\"]*)\"[^>]*name=\"{escaped}\"");
        }

        Assert.True(match.Success, $"Hidden field {name} not found on the login page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void Collect(HttpResponseMessage response, Dictionary<string, string> cookies)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            var pair = value.Split(';', 2)[0];
            var separator = pair.IndexOf('=');
            cookies[pair[..separator]] = pair[(separator + 1)..];
        }
    }

    private static string Header(Dictionary<string, string> cookies) =>
        string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
}
