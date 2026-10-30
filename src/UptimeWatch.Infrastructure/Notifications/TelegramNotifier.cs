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

        var downtime = incident.Duration is { } d ? DurationFormatter.Format(d) : "неизвестно";
        var text = BuildText($"🟢 Сайт снова доступен: {monitor.Url}", "\nВремя недоступности: ", downtime);
        await sender.SendAsync(chatId, text, ct);
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
