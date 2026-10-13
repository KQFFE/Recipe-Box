using RecipeBox.Web.Services;

namespace RecipeBox.Tests;

public sealed class LocalRecipeImageStorageTests
{
    [Fact]
    public async Task ImageFiles_AreStoredPerOwnerAndCanBeReadAndRemoved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"recipe-image-storage-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalRecipeImageStorage(directory);
            var image = new byte[] { 0xFF, 0xD8, 0xFF, 0x12 };
            var stored = await storage.SaveAsync("owner-one", image, CancellationToken.None);

            Assert.Equal("image/jpeg", stored.ContentType);
            Assert.Matches("^[0-9a-f]{32}$", stored.StorageKey);
            await using (var ownerStream = await storage.OpenReadAsync(
                             "owner-one",
                             stored.StorageKey,
                             CancellationToken.None))
            {
                Assert.NotNull(ownerStream);
                using var buffer = new MemoryStream();
                await ownerStream!.CopyToAsync(buffer);
                Assert.Equal(image, buffer.ToArray());
            }

            Assert.Null(await storage.OpenReadAsync("owner-two", stored.StorageKey, CancellationToken.None));

            await storage.DeleteAsync("owner-one", stored.StorageKey, CancellationToken.None);
            Assert.Null(await storage.OpenReadAsync("owner-one", stored.StorageKey, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenReadAsync_RejectsStoragePathTraversal()
    {
        var storage = new LocalRecipeImageStorage(Path.GetTempPath());

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.OpenReadAsync("owner", "..\\outside", CancellationToken.None));
    }
}
