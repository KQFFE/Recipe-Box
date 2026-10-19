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
    DateTime UpdatedAtUtc,
    IReadOnlyList<string>? SectionOrder = null,
    IReadOnlyList<string>? FullWidthSections = null)
{
    public static RecipeResponse FromRecipe(Recipe recipe, RecipeLayoutPreference? layout = null) =>
        new(
            recipe.Id,
            recipe.Title,
            recipe.Description,
            recipe.LanguageCode,
            RecipeContent.ToLines(recipe.Ingredients),
            RecipeContent.ToLines(recipe.Instructions),
            recipe.CreatedAtUtc,
            recipe.UpdatedAtUtc,
            layout is null ? null : SplitKeys(layout.SectionOrder),
            layout is null ? null : SplitKeys(layout.FullWidthSections));

    private static string[] SplitKeys(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries);
}

public sealed record RecipeListResponse(
    IReadOnlyList<RecipeResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record RecipeShareResponse(bool Active, string? Url);
