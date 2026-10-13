using System.Security.Cryptography;
using System.Text;

namespace RecipeBox.Web.Services;

public sealed class LocalRecipeImageStorage(string storagePath) : IRecipeImageStorage
{
    private readonly string rootPath = Path.GetFullPath(storagePath);

    public async Task<StoredRecipeImage> SaveAsync(
        string ownerId,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(imageBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var contentType = RecipeImageUpload.GetContentType(imageBytes)
            ?? throw new ArgumentException("The image must be a supported JPEG, PNG, or WebP.", nameof(imageBytes));
        var storageKey = Guid.NewGuid().ToString("N");
        var ownerDirectory = GetOwnerDirectory(ownerId);
        Directory.CreateDirectory(ownerDirectory);
        var imagePath = Path.Combine(ownerDirectory, storageKey);

        try
        {
            await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);
        }
        catch
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }

            throw;
        }

        return new StoredRecipeImage(storageKey, contentType);
    }

    public Task<Stream?> OpenReadAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imagePath = GetImagePath(ownerId, storageKey);
        Stream? stream = File.Exists(imagePath)
            ? new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string ownerId,
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var imagePath = GetImagePath(ownerId, storageKey);
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        return Task.CompletedTask;
    }

    private string GetOwnerDirectory(string ownerId)
    {
        var ownerKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId)));
        return Path.Combine(rootPath, ownerKey);
    }

    private string GetImagePath(string ownerId, string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!Guid.TryParseExact(storageKey, "N", out _))
        {
            throw new ArgumentException("The image storage key is invalid.", nameof(storageKey));
        }

        return Path.Combine(GetOwnerDirectory(ownerId), storageKey);
    }
}
