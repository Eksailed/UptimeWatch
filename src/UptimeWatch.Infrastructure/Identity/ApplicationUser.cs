using Microsoft.AspNetCore.Identity;

namespace UptimeWatch.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? TelegramChatId { get; set; }
}
