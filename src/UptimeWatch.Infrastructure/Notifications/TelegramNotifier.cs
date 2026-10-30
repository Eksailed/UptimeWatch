using System.Globalization;
using System.Text;
using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>
/// Sends alerts to the monitor owner's Telegram chat. Sender failures propagate: CheckProcessor catches and logs
/// them, so a Telegram outage cannot break the check loop. Messages are plain text (no parse_mode), so a URL or
/// an error string cannot inject markup.
/// </summary>
public sealed class TelegramNotifier(ITelegramSender sender, IUserProfileStore profiles) : INotifier
{
    /// <summary>Telegram's limit for the text of one message.</summary>
    public const int MaxMessageLength = 4096;

    public async Task NotifyDownAsync(SiteMonitor monitor, Incident incident, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(incident);

        if (await GetChatIdAsync(monitor, ct) is not { } chatId)
        {
            return;
        }

        var error = string.IsNullOrWhiteSpace(incident.LastError) ? "неизвестна" : incident.LastError;
        var text = BuildText($"🔴 Сайт недоступен: {monitor.Url}", "\nОшибка: ", error);
        await sender.SendAsync(chatId, text, ct);
    }

    public async Task NotifyRecoveredAsync(SiteMonitor monitor, Incident incident, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(incident);

        if (await GetChatIdAsync(monitor, ct) is not { } chatId)
        {
            return;
        }

        var downtime = incident.Duration is { } d ? FormatDuration(d) : "неизвестно";
        var text = BuildText($"🟢 Сайт снова доступен: {monitor.Url}", "\nВремя недоступности: ", downtime);
        await sender.SendAsync(chatId, text, ct);
    }

    /// <summary>
    /// Russian, at most the two largest non-zero units, e.g. "1 ч 5 мин", "45 с". Zero units in between are skipped,
    /// so 1 day 0 h 0 min 30 s reads "1 д 30 с" (each number carries its own unit, so this is unambiguous).
    /// </summary>
    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var parts = new List<string>(4);
        if (duration.Days > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Days} д"));
        }

        if (duration.Hours > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Hours} ч"));
        }

        if (duration.Minutes > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes} мин"));
        }

        if (duration.Seconds > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Seconds} с"));
        }

        return parts.Count == 0 ? "меньше секунды" : string.Join(' ', parts.Take(2));
    }

    /// <summary>Header (with the URL) is kept; the variable part is truncated so the whole text fits Telegram's limit.</summary>
    internal static string BuildText(string header, string label, string detail)
    {
        if (header.Length + label.Length >= MaxMessageLength)
        {
            return Truncate(header, MaxMessageLength);
        }

        var text = new StringBuilder(header).Append(label);
        return text.Append(Truncate(detail, MaxMessageLength - header.Length - label.Length)).ToString();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut] + "…";
    }

    private async Task<string?> GetChatIdAsync(SiteMonitor monitor, CancellationToken ct)
    {
        var chatId = await profiles.GetTelegramChatIdAsync(monitor.OwnerId, ct);
        return string.IsNullOrWhiteSpace(chatId) ? null : chatId;
    }
}
