using Microsoft.AspNetCore.Identity;
using UptimeWatch.Application;

namespace UptimeWatch.Infrastructure.Identity;

public sealed class UserProfileStore(UserManager<ApplicationUser> users) : IUserProfileStore
{
    public async Task<string?> GetTelegramChatIdAsync(string userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId);
        return TelegramChatId.Normalize(user?.TelegramChatId);
    }

    public async Task SetTelegramChatIdAsync(string userId, string? chatId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = TelegramChatId.Normalize(chatId);
        if (!TelegramChatId.TryValidate(normalized, out var error))
        {
            throw new ArgumentException(error, nameof(chatId));
        }

        var user = await users.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User was not found.");
        user.TelegramChatId = normalized;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to save the Telegram chat id: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }
}
