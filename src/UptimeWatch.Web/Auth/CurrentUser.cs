using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using UptimeWatch.Application;

namespace UptimeWatch.Web.Auth;

/// <summary>
/// Resolves the signed-in user for the current request or Blazor circuit.
/// Throws <see cref="InvalidOperationException"/> for anonymous callers: every page and endpoint that uses it
/// must be protected with <c>[Authorize]</c>, so reaching it anonymously is a programming error, not a user error.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContext, AuthenticationStateProvider authenticationState) : ICurrentUser
{
    public string UserId
    {
        get
        {
            // Static SSR / plain HTTP requests carry the user in HttpContext; inside an interactive circuit
            // HttpContext is not reliable, so fall back to the circuit's AuthenticationStateProvider.
            var principal = httpContext.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                principal = TryGetPrincipalFromProvider();
            }

            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new InvalidOperationException("There is no authenticated user in the current context.");
            }

            return principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("The authenticated user has no NameIdentifier claim.");
        }
    }

    private ClaimsPrincipal? TryGetPrincipalFromProvider()
    {
        try
        {
            // The state task is already completed once the provider is initialised (SSR and circuits),
            // so we never block on I/O here.
            var task = authenticationState.GetAuthenticationStateAsync();
            return task.IsCompletedSuccessfully ? task.Result.User : null;
        }
        catch (InvalidOperationException)
        {
            // Provider not initialised for this scope (no request, no circuit): treat as anonymous.
            return null;
        }
    }
}
