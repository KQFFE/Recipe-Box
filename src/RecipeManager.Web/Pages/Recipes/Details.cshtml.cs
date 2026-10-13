using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class DetailsModel(ApplicationDbContext db) : RecipePageModel
{
    public Recipe Recipe { get; private set; } = null!;

    public IReadOnlyList<string> Ingredients { get; private set; } = [];

    public IReadOnlyList<string> Instructions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var recipe = await db.Recipes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                recipe => recipe.Id == id && recipe.OwnerId == CurrentUserId,
                cancellationToken);

        if (recipe is null)
        {
            return NotFound();
        }

        Recipe = recipe;
        Ingredients = RecipeContent.ToLines(Recipe.Ingredients);
        Instructions = RecipeContent.ToLines(Recipe.Instructions);

        return Page();
    }
}
