namespace RecipeBox.Web.Services;

public static class RecipeImageStorageCleanup
{
    public static async Task<bool> TryDeleteAsync(
        IRecipeImageStorage storage,
        string ownerId,
        string storageKey,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(ownerId, storageKey, cancellationToken);
            return true;
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not remove stored recipe image {StorageKey}.", storageKey);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(exception, "Could not remove stored recipe image {StorageKey}.", storageKey);
            return false;
        }
    }
}
