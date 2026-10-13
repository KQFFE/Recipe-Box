using RecipeManager.Web.Models;

namespace RecipeManager.Web.Services;

public interface IRecipeUrlImporter
{
    Task<RecipeDraft> ImportAsync(Uri recipeUri, CancellationToken cancellationToken);
}
