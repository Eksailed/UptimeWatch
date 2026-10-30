using Microsoft.AspNetCore.Identity;

namespace UptimeWatch.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string? TelegramChatId { get; set; }
}
