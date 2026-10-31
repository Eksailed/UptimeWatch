using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace UptimeWatch.Domain;

public static class UrlPolicy
{
    /// <summary>
    /// Static URL checks only. Host names are not resolved here; DNS results are checked
    /// at connection time. IP-literal hosts and "localhost" are checked directly.
    /// </summary>
    public static bool TryValidate(string? url, [NotNullWhen(false)] out ErrorInfo? error)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            error = new ErrorInfo(ErrorCode.UrlRequired, "URL is required.");
            return false;
        }

        if (url.Length > MonitorLimits.MaxUrlLength)
        {
            error = new ErrorInfo(
                ErrorCode.UrlTooLong, $"URL must not be longer than {MonitorLimits.MaxUrlLength} characters.", MonitorLimits.MaxUrlLength);
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = new ErrorInfo(ErrorCode.UrlNotAbsolute, "URL is not a valid absolute URL.");
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = new ErrorInfo(ErrorCode.UrlSchemeNotAllowed, "Only http and https URLs are allowed.");
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = new ErrorInfo(ErrorCode.UrlHasCredentials, "URLs with embedded credentials are not allowed.");
            return false;
        }

        var host = uri.Host.TrimEnd('.');
        if (host.Length == 0)
        {
            error = new ErrorInfo(ErrorCode.UrlNoHost, "URL has no host.");
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            error = new ErrorInfo(ErrorCode.UrlLocalAddress, "Local addresses are not allowed.");
            return false;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && !IpAddressPolicy.IsPublic(ip))
        {
            error = new ErrorInfo(ErrorCode.UrlPrivateAddress, "Private, loopback and link-local addresses are not allowed.");
            return false;
        }

        error = null;
        return true;
    }
}
