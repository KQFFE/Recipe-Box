using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

public abstract class RecipePageModel : PageModel
{
    protected string? CurrentUserIdOrNull => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected bool IsGuest => GuestAccountService.IsGuest(User);

    protected string CurrentUserId =>
        CurrentUserIdOrNull
        ?? throw new InvalidOperationException("An authenticated recipe owner is required.");
}
