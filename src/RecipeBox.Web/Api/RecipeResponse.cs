using RecipeBox.Web.Models;

namespace RecipeBox.Web.Api;

public sealed record RecipeResponse(
    int Id,
    string Title,
    string? Description,
    string LanguageCode,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static RecipeResponse FromRecipe(Recipe recipe) =>
        new(
            recipe.Id,
            recipe.Title,
            recipe.Description,
            recipe.LanguageCode,
            RecipeContent.ToLines(recipe.Ingredients),
            RecipeContent.ToLines(recipe.Instructions),
            recipe.CreatedAtUtc,
            recipe.UpdatedAtUtc);
}

public sealed record RecipeListResponse(
    IReadOnlyList<RecipeResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
