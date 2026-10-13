using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;
using RecipeManager.Web.Services;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class DeleteModel(
    ApplicationDbContext db,
    IRecipeImageStorage imageStorage,
    ILogger<DeleteModel> logger) : RecipePageModel
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
        if (!string.IsNullOrWhiteSpace(recipe.StoredImageKey)
            && !await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                recipe.OwnerId,
                recipe.StoredImageKey,
                logger,
                CancellationToken.None))
        {
            TempData["ImageCleanupWarning"] = "The recipe was deleted, but its uploaded image could not be removed.";
        }

        return RedirectToPage("Index");
    }

    private Task<Recipe?> FindOwnedRecipeAsync(CancellationToken cancellationToken) =>
        db.Recipes.AsNoTracking().FirstOrDefaultAsync(
            recipe => recipe.Id == Id && recipe.OwnerId == CurrentUserId,
            cancellationToken);
}
