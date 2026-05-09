using Microsoft.AspNetCore.Identity;
using BorderLink.Shared.Entities;

namespace BorderLink.Server.Components.Account;

internal sealed class IdentityUserAccessor(UserManager<BorderLinkUser> userManager, IdentityRedirectManager redirectManager)
{
    public async Task<BorderLinkUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);

        if (user is null)
        {
            redirectManager.RedirectToWithStatus("Account/InvalidUser", $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.", context);
        }

        return user;
    }
}
