using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using UptimeWatch.Web.Auth;

namespace UptimeWatch.Integration.Tests.Web;

public sealed class CurrentUserTests
{
    private static ClaimsPrincipal Authenticated(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], authenticationType: "test"));

    private static IHttpContextAccessor Http(ClaimsPrincipal? user) =>
        new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };

    private static AuthenticationStateProvider Provider(ClaimsPrincipal user)
    {
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns(Task.FromResult(new AuthenticationState(user)));
        return provider;
    }

    [Fact]
    public void Returns_id_from_http_context()
    {
        var current = new CurrentUser(Http(Authenticated("user-1")), Provider(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Equal("user-1", current.UserId);
    }

    [Fact]
    public void Falls_back_to_authentication_state_inside_a_circuit()
    {
        var current = new CurrentUser(Http(null), Provider(Authenticated("user-2")));

        Assert.Equal("user-2", current.UserId);
    }

    [Fact]
    public void Throws_for_anonymous_user()
    {
        var current = new CurrentUser(Http(null), Provider(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Throws<InvalidOperationException>(() => current.UserId);
    }

    [Fact]
    public void Throws_when_provider_is_not_initialised()
    {
        var provider = Substitute.For<AuthenticationStateProvider>();
        provider.GetAuthenticationStateAsync().Returns<Task<AuthenticationState>>(_ => throw new InvalidOperationException("not initialised"));
        var current = new CurrentUser(Http(null), provider);

        Assert.Throws<InvalidOperationException>(() => current.UserId);
    }
}
