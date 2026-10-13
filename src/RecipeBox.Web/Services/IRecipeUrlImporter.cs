using RecipeBox.Web.Models;

namespace RecipeBox.Web.Services;

public interface IRecipeUrlImporter
{
    Task<RecipeDraft> ImportAsync(Uri recipeUri, CancellationToken cancellationToken);
}
