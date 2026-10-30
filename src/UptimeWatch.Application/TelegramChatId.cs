using System.Text.RegularExpressions;

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
    public static bool TryValidate(string? normalized, out string? error)
    {
        if (normalized is null)
        {
            error = null;
            return true;
        }

        if (normalized.Length > MaxLength || !Pattern().IsMatch(normalized))
        {
            error = "Некорректный chat_id: ожидается число (например, 123456789 или -1001234567890) или @имя_канала.";
            return false;
        }

        error = null;
        return true;
    }
}
