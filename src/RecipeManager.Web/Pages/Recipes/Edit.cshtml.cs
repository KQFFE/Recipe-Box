using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class EditModel(ApplicationDbContext db) : RecipePageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        Input = new RecipeFormModel
        {
            Title = recipe.Title,
            Description = recipe.Description,
            ImageUrl = recipe.ImageUrl,
            Ingredients = recipe.Ingredients,
            Instructions = recipe.Instructions
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        recipe.Title = Input.Title.Trim();
        recipe.Description = Input.Description?.Trim();
        recipe.ImageUrl = Input.ImageUrl?.Trim();
        recipe.Ingredients = RecipeContent.NormalizeLines(Input.Ingredients ?? string.Empty);
        recipe.Instructions = RecipeContent.NormalizeLines(Input.Instructions ?? string.Empty);
        recipe.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("Details", new { id = recipe.Id });
    }

    private Task<Recipe?> FindOwnedRecipeAsync(CancellationToken cancellationToken) =>
        db.Recipes.FirstOrDefaultAsync(
            recipe => recipe.Id == Id && recipe.OwnerId == CurrentUserId,
            cancellationToken);
}
