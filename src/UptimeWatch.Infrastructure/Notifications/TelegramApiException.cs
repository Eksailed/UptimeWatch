namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>A failed Telegram call. The message never contains the request URL or the bot token.</summary>
public sealed class TelegramApiException : Exception
{
    public TelegramApiException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code returned by Telegram, or null when there was no response.</summary>
    public int? StatusCode { get; }
}
