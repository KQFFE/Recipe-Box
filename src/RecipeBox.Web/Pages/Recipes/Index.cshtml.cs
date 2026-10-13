using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;

namespace RecipeBox.Web.Pages.Recipes;

[Authorize]
public sealed class IndexModel(ApplicationDbContext db) : RecipePageModel
{
    public IReadOnlyList<Recipe> Recipes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Recipes = await db.Recipes
            .AsNoTracking()
            .Where(recipe => recipe.OwnerId == CurrentUserId)
            .OrderBy(recipe => recipe.Title)
            .ToListAsync(cancellationToken);
    }
}
