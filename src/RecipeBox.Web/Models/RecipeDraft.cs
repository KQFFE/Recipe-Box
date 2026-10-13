namespace RecipeBox.Web.Models;

public sealed record RecipeDraft(
    string Title,
    string Ingredients,
    string Instructions,
    string? Description = null,
    string? ImageUrl = null);
