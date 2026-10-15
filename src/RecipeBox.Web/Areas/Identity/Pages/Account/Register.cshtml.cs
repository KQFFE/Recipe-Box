using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RecipeBox.Web.Areas.Identity.Pages.Account;

public sealed class RegisterModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IEmailSender emailSender,
    IWebHostEnvironment environment) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
        ReturnUrl = GetSafeReturnUrl(ReturnUrl);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl = GetSafeReturnUrl(ReturnUrl);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = new IdentityUser
        {
            UserName = Input.Email,
            Email = Input.Email
        };
        var result = await userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        if (userManager.Options.SignIn.RequireConfirmedAccount)
        {
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var confirmationUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new
                {
                    area = "Identity",
                    userId = user.Id,
                    code,
                    returnUrl = ReturnUrl
                },
                protocol: Request.Scheme)
                ?? throw new InvalidOperationException("The email confirmation URL could not be generated.");

            await emailSender.SendEmailAsync(
                Input.Email,
                "Confirm your Recipe Box account",
                $"Confirm your account by <a href=\"{HtmlEncoder.Default.Encode(confirmationUrl)}\">clicking here</a>.");

            if (environment.IsDevelopment())
            {
                TempData["EmailConfirmationLink"] = confirmationUrl;
            }

            return RedirectToPage(
                "RegisterConfirmation",
                new { email = Input.Email, returnUrl = ReturnUrl });
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(ReturnUrl ?? Url.Content("~/"));
    }

    private string? GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
