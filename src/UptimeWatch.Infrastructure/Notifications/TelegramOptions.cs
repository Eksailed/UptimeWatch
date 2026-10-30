using System.Text.RegularExpressions;

namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>
/// Telegram settings from configuration section "Telegram". The token comes only from configuration
/// (user secrets / environment variable Telegram__BotToken). A class with a hiding ToString, not a record:
/// a record would print the token.
/// </summary>
public sealed partial class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    // BotFather tokens look like 123456789:AA... ; anything else could change the request path.
    [GeneratedRegex(@"^[0-9]+:[A-Za-z0-9_-]+\z")]
    private static partial Regex TokenPattern();

    public static bool IsValidToken(string? token) => token is not null && TokenPattern().IsMatch(token);

    public override string ToString() => nameof(TelegramOptions);
}
