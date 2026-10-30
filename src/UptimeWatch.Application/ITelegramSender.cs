namespace UptimeWatch.Application;

/// <summary>Sends one plain-text message to a Telegram chat through the bot configured for the application.</summary>
public interface ITelegramSender
{
    /// <summary>Throws if Telegram rejects the message or cannot be reached. Exception messages never contain the bot token.</summary>
    Task SendAsync(string chatId, string text, CancellationToken ct);
}
