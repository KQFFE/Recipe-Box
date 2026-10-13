using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class DeleteModel(ApplicationDbContext db) : RecipePageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Recipe Recipe { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        Recipe = recipe;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("Index");
    }

    private Task<Recipe?> FindOwnedRecipeAsync(CancellationToken cancellationToken) =>
        db.Recipes.AsNoTracking().FirstOrDefaultAsync(
            recipe => recipe.Id == Id && recipe.OwnerId == CurrentUserId,
            cancellationToken);
}
