using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RecipeBox.Web.Areas.Identity.Pages.Account;

public sealed class RegisterConfirmationModel(IWebHostEnvironment environment) : PageModel
{
    public string? Email { get; private set; }

    public string? ReturnUrl { get; private set; }

    public string? ConfirmationLink { get; private set; }

    public void OnGet(string? email, string? returnUrl)
    {
        Email = email;
        ReturnUrl = GetSafeReturnUrl(returnUrl);
        if (environment.IsDevelopment())
        {
            ConfirmationLink = TempData["EmailConfirmationLink"] as string;
        }
    }

    private string? GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;
}
