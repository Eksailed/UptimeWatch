using Microsoft.AspNetCore.Localization;

namespace UptimeWatch.Web.Localization;

public static class CultureEndpoints
{
    public const string Route = "/culture/set";

    /// <summary>
    /// GET /culture/set?culture=ru|en&amp;redirectUri=/path : stores the language in the culture cookie and redirects
    /// back. Only the supported languages are accepted (400 otherwise) and only local redirect targets are followed.
    /// </summary>
    public static IEndpointConventionBuilder MapCultureEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(Route, (HttpContext context, string? culture, string? redirectUri) =>
        {
            if (SupportedLanguages.Find(culture) is not { } language)
            {
                return Results.BadRequest();
            }

            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    SameSite = SameSiteMode.Lax,
                    HttpOnly = false,
                    Secure = context.Request.IsHttps,
                    IsEssential = true,
                    Path = "/"
                });

            context.Response.Headers.CacheControl = "no-store";
            return Results.LocalRedirect(IsLocal(redirectUri) ? redirectUri! : "/");
        });

    /// <summary>
    /// True only for a path on this site: it starts with a single '/', has no backslash (browsers treat it as '/',
    /// so "/\host" would be "//host") and no control or non-ASCII characters (header injection; Kestrel fails to write a non-ASCII Location header).
    /// </summary>
    internal static bool IsLocal(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || uri[0] != '/')
        {
            return false;
        }

        if (uri.Length > 1 && uri[1] == '/')
        {
            return false;
        }

        return !uri.Any(c => c == '\\' || char.IsControl(c) || c > 127);
    }
}
