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

    public IReadOnlySet<string> FullWidthSections { get; private set; } = new HashSet<string>();

    public IReadOnlyList<RecipeLayoutSection> LayoutSections { get; private set; } = [];

    public RecipePresentation Presentation { get; private set; } = null!;

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
        var preference = await db.RecipeLayouts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                layout => layout.RecipeId == id && layout.OwnerId == CurrentUserId,
                cancellationToken);
        var layoutSettings = RecipeLayoutDefinition.FromPreference(Recipe, preference);
        LayoutSections = layoutSettings.Sections;
        FullWidthSections = layoutSettings.FullWidthSections;
        Presentation = new RecipePresentation(
            Recipe,
            Ingredients,
            Instructions,
            LayoutSections,
            FullWidthSections);

        return Page();
    }
}
