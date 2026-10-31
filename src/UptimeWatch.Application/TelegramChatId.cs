using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using UptimeWatch.Domain;

namespace UptimeWatch.Application;

/// <summary>Normalisation and validation of the Telegram chat id a user enters in the profile.</summary>
public static partial class TelegramChatId
{
    public const int MaxLength = 64;

    // A numeric id (users and groups; supergroups and channels start with '-'), or @channelname.
    [GeneratedRegex(@"^(-?[0-9]+|@[A-Za-z][A-Za-z0-9_]{1,63})\z")]
    private static partial Regex Pattern();

    /// <summary>Trims the input; blank becomes null (meaning "no chat id").</summary>
    public static string? Normalize(string? input)
    {
        var trimmed = input?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>A null (cleared) value is valid. Expects an already normalised value.</summary>
    public static bool TryValidate(string? normalized, [NotNullWhen(false)] out ErrorInfo? error)
    {
        if (normalized is null)
        {
            error = null;
            return true;
        }

        if (normalized.Length > MaxLength || !Pattern().IsMatch(normalized))
        {
            error = new ErrorInfo(
                ErrorCode.TelegramChatIdInvalid,
                "Invalid chat_id: expected a number (e.g. 123456789 or -1001234567890) or @channel_name.");
            return false;
        }

        error = null;
        return true;
    }
}
