using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RecipeBox.Web.Areas.Identity.Pages.Account;

public sealed class ConfirmEmailModel(UserManager<IdentityUser> userManager) : PageModel
{
    public string StatusMessage { get; private set; } = "The confirmation link is invalid.";

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code, string? returnUrl)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return Page();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Page();
        }

        var result = await userManager.ConfirmEmailAsync(user, code);
        if (!result.Succeeded)
        {
            return Page();
        }

        return RedirectToPage("./Login", new { returnUrl = ReturnUrl });
    }

    private string? GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;
}
