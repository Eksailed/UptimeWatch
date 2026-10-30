using System.Net;

namespace UptimeWatch.Domain;

public static class UrlPolicy
{
    /// <summary>
    /// Static URL checks only. Host names are not resolved here; DNS results are checked
    /// at connection time. IP-literal hosts and "localhost" are checked directly.
    /// </summary>
    public static bool TryValidate(string? url, out string? error)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL is required.";
            return false;
        }

        if (url.Length > MonitorLimits.MaxUrlLength)
        {
            error = $"URL must not be longer than {MonitorLimits.MaxUrlLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL is not a valid absolute URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Only http and https URLs are allowed.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "URLs with embedded credentials are not allowed.";
            return false;
        }

        var host = uri.Host.TrimEnd('.');
        if (host.Length == 0)
        {
            error = "URL has no host.";
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            error = "Local addresses are not allowed.";
            return false;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && !IpAddressPolicy.IsPublic(ip))
        {
            error = "Private, loopback and link-local addresses are not allowed.";
            return false;
        }

        error = null;
        return true;
    }
}
