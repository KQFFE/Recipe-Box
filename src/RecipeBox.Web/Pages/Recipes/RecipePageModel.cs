using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RecipeBox.Web.Pages.Recipes;

public abstract class RecipePageModel : PageModel
{
    protected string CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("An authenticated recipe owner is required.");
}
