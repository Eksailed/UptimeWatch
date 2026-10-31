namespace UptimeWatch.Web.Components;

/// <summary>Display helpers for a monitored URL.</summary>
internal static class MonitorUrl
{
    /// <summary>The host of the URL for use as a card title; falls back to the whole text when it is not an absolute URL.</summary>
    public static string Domain(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : url;
}
