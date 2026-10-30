using UptimeWatch.Application;

namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>Registered when no bot token is configured; callers are expected to check <see cref="TelegramOptions.IsConfigured"/> first.</summary>
public sealed class NullTelegramSender : ITelegramSender
{
    public Task SendAsync(string chatId, string text, CancellationToken ct) =>
        throw new InvalidOperationException("Telegram is not configured on the server.");
}
