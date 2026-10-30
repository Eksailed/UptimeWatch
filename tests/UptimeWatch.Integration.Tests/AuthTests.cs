using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "Test-Passw0rd!";

    private WebAppFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebAppFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Anonymous_request_to_monitors_page_redirects_to_login()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/monitors");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Registered_user_can_sign_in_with_password()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        var created = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email }, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Code)));

        // PasswordSignInAsync issues a cookie, so it needs an HttpContext to write to.
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var good = await signIn.PasswordSignInAsync(email, Password, isPersistent: false, lockoutOnFailure: false);
        var bad = await signIn.PasswordSignInAsync(email, Password + "x", isPersistent: false, lockoutOnFailure: false);

        Assert.True(good.Succeeded);
        Assert.False(bad.Succeeded);
    }

    [Fact]
    public async Task Duplicate_email_registration_is_rejected()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var first = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email }, Password);
        // Different user name, same e-mail: only RequireUniqueEmail can reject this one.
        var second = await users.CreateAsync(new ApplicationUser { UserName = "other-" + email, Email = email }, Password);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
    }

    [Fact]
    public async Task App_starts_without_telegram_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
