using System.ComponentModel.DataAnnotations;

namespace RecipeBox.Web.Api;

public sealed class RecipeDraftRequest
{
    [Required]
    [StringLength(2048)]
    public string Url { get; init; } = string.Empty;
}

public sealed record RecipeDraftResponse(
    string Title,
    string? Description,
    string? ImageUrl,
    string LanguageCode,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions)
{
    public static RecipeDraftResponse FromDraft(RecipeBox.Web.Models.RecipeDraft draft)
    {
        var recipeText = string.Join(
            Environment.NewLine,
            draft.Title,
            draft.Description,
            draft.Ingredients,
            draft.Instructions);
        return new RecipeDraftResponse(
            draft.Title,
            draft.Description,
            draft.ImageUrl,
            RecipeBox.Web.Models.RecipeLanguage.ResolveCode(draft.LanguageCode, recipeText),
            RecipeBox.Web.Models.RecipeContent.ToLines(draft.Ingredients),
            RecipeBox.Web.Models.RecipeContent.ToLines(draft.Instructions));
    }
}
