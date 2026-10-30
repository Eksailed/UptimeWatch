using System.Collections.Concurrent;

namespace UptimeWatch.Application;

/// <summary>
/// Sends the "test message" from the profile page with a per-user cooldown, so one user cannot use the bot as a
/// message cannon. The cooldown starts when a send is attempted (also if Telegram then fails). In-memory; register as a singleton.
/// </summary>
public sealed class TelegramTestMessageService(ITelegramSender sender, TimeProvider time)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    public const string TestText = "Тестовое сообщение от UptimeWatch: уведомления настроены.";

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAttempt = new();

    /// <summary>
    /// Returns null after the message was sent, or the time left until the next attempt is allowed (nothing is sent then).
    /// Sender exceptions propagate.
    /// </summary>
    public async Task<TimeSpan?> SendTestAsync(string userId, string chatId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = time.GetUtcNow();

        if (!TryStart(userId, now, out var retryAfter))
        {
            return retryAfter;
        }

        await sender.SendAsync(chatId, TestText, ct);
        return null;
    }

    private bool TryStart(string userId, DateTimeOffset now, out TimeSpan retryAfter)
    {
        // Drop expired entries so the dictionary does not grow with every user that ever pressed the button.
        foreach (var (key, at) in _lastAttempt)
        {
            if (now - at >= Cooldown)
            {
                _lastAttempt.TryRemove(new KeyValuePair<string, DateTimeOffset>(key, at));
            }
        }

        while (true)
        {
            if (!_lastAttempt.TryGetValue(userId, out var last))
            {
                if (_lastAttempt.TryAdd(userId, now))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }

                continue;
            }

            if (now - last < Cooldown)
            {
                retryAfter = Cooldown - (now - last);
                return false;
            }

            if (_lastAttempt.TryUpdate(userId, now, last))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }
}
