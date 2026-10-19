namespace RecipeBox.Mobile.Core;

public interface IRecipeProfileApi
{
    Task<RecipeListResponse> GetRecipesAsync(
        Uri serverAddress,
        int page,
        CancellationToken cancellationToken = default);

    Task<RecipeResponse> GetRecipeAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default);

    Task<RecipeResponse> CreateRecipeAsync(
        Uri serverAddress,
        RecipeWriteRequest recipe,
        CancellationToken cancellationToken = default);

    Task<RecipeResponse> UpdateRecipeAsync(
        Uri serverAddress,
        int recipeId,
        RecipeWriteRequest recipe,
        CancellationToken cancellationToken = default);

    Task DeleteRecipeAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default);

    Task<RecipeImageResponse?> GetRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default);

    Task UploadRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        byte[] imageBytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default);
}

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
    IReadOnlyList<string>? FullWidthSections = null);

public sealed record RecipeListResponse(
    IReadOnlyList<RecipeResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record RecipeImageResponse(byte[] Bytes, string ContentType);

public sealed record RecipeWriteRequest(
    string Title,
    string? Description,
    string LanguageCode,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<string>? SectionOrder = null,
    IReadOnlyList<string>? FullWidthSections = null);
