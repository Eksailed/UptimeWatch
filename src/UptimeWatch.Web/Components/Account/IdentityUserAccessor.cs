using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Web.Localization;

namespace UptimeWatch.Web.Components.Account;

internal sealed class IdentityUserAccessor(
    UserManager<ApplicationUser> userManager,
    IdentityRedirectManager redirectManager,
    IStringLocalizer<SharedResource> localizer)
{
    public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/InvalidUser",
                localizer.ErrorMessage("Account_UserLoadFailed", userManager.GetUserId(context.User) ?? ""),
                context);
        }

        return user;
    }
}
