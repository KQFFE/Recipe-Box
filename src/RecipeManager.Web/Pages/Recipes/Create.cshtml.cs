using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class CreateModel(ApplicationDbContext db) : RecipePageModel
{
    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            Title = Input.Title.Trim(),
            Description = Input.Description?.Trim(),
            ImageUrl = Input.ImageUrl?.Trim(),
            Ingredients = RecipeContent.NormalizeLines(Input.Ingredients ?? string.Empty),
            Instructions = RecipeContent.NormalizeLines(Input.Instructions ?? string.Empty),
            OwnerId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("Details", new { id = recipe.Id });
    }
}
