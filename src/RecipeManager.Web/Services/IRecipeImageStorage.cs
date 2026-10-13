namespace RecipeManager.Web.Services;

public sealed record StoredRecipeImage(string StorageKey, string ContentType);

public interface IRecipeImageStorage
{
    Task<StoredRecipeImage> SaveAsync(
        string ownerId,
        byte[] imageBytes,
        CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken);
}
