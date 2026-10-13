using System.Collections.Concurrent;
using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class TestRecipeImageStorage : IRecipeImageStorage
{
    private readonly ConcurrentDictionary<(string OwnerId, string StorageKey), (byte[] Bytes, string ContentType)> files = new();

    public int Count => files.Count;

    public Task<StoredRecipeImage> SaveAsync(
        string ownerId,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentType = RecipeImageUpload.GetContentType(imageBytes)
            ?? throw new ArgumentException("The image format is unsupported.", nameof(imageBytes));
        var storageKey = Guid.NewGuid().ToString("N");
        files[(ownerId, storageKey)] = (imageBytes.ToArray(), contentType);
        return Task.FromResult(new StoredRecipeImage(storageKey, contentType));
    }

    public Task<Stream?> OpenReadAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream? stream = files.TryGetValue((ownerId, storageKey), out var file)
            ? new MemoryStream(file.Bytes, writable: false)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        files.TryRemove((ownerId, storageKey), out _);
        return Task.CompletedTask;
    }
}
