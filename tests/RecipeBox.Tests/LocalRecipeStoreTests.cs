using RecipeBox.Mobile.Core;

namespace RecipeBox.Tests;

public sealed class LocalRecipeStoreTests
{
    [Fact]
    public async Task Recipes_ArePersistedAndCanBeManagedWithoutAProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-recipes-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "recipes.db");
        var imageDirectory = Path.Combine(directory, "images");
        var recipe = new StoredRecipe
        {
            ServerId = 7,
            Title = "Offline soup",
            Description = "Made on this device.",
            LanguageCode = "en",
            Ingredients = ["tomatoes", "onion"],
            Instructions = ["Chop", "Cook"]
        };

        try
        {
            await using (var store = new LocalRecipeStore(databasePath, imageDirectory))
            {
                await store.SaveLocalAsync(recipe);
                var saved = await store.GetByLocalIdAsync(recipe.LocalId);
                Assert.NotNull(saved);
                Assert.True(saved.IsDirty);
                Assert.Equal(["tomatoes", "onion"], saved.Ingredients);

                saved.Title = "Edited offline soup";
                await store.SaveLocalAsync(saved);
                await store.DeleteAsync(saved.LocalId);

                Assert.Empty(await store.GetRecipesAsync());
                Assert.True((await store.GetAllAsync()).Single().IsDeleted);
            }

            await using var reopenedStore = new LocalRecipeStore(databasePath, imageDirectory);
            var persisted = Assert.Single(await reopenedStore.GetAllAsync());
            Assert.Equal("Edited offline soup", persisted.Title);
            Assert.True(persisted.IsDeleted);
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
    public async Task RemovingProfileRecipes_PreservesLocalOnlyRecipes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-recipes-{Guid.NewGuid():N}");
        try
        {
            await using var store = new LocalRecipeStore(
                Path.Combine(directory, "recipes.db"),
                Path.Combine(directory, "images"));
            await store.SaveLocalAsync(new StoredRecipe
            {
                Title = "Only on device",
                Ingredients = ["water"],
                Instructions = ["Boil"]
            });
            await store.SaveLocalAsync(new StoredRecipe
            {
                Title = "Second device-only recipe",
                Ingredients = ["tea"],
                Instructions = ["Steep"]
            });
            await store.SaveRemoteAsync(new StoredRecipe
            {
                ServerId = 12,
                Title = "From profile"
            });

            await store.RemoveProfileRecipesAsync();

            var remaining = await store.GetRecipesAsync();
            Assert.Equal(2, remaining.Count);
            Assert.Contains(remaining, recipe => recipe.Title == "Only on device");
            Assert.Contains(remaining, recipe => recipe.Title == "Second device-only recipe");
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
    public async Task CoverImages_AreStoredLocallyAndCanBeRemoved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-recipes-{Guid.NewGuid():N}");
        try
        {
            await using var store = new LocalRecipeStore(
                Path.Combine(directory, "recipes.db"),
                Path.Combine(directory, "images"));
            var recipe = new StoredRecipe
            {
                Title = "Offline soup",
                Ingredients = ["tomatoes"],
                Instructions = ["Cook"]
            };
            await store.SaveLocalAsync(recipe);
            var image = new byte[] { 0xFF, 0xD8, 0xFF, 0x10 };

            await store.SaveCoverImageAsync(recipe.LocalId, image, "image/jpeg");
            var saved = await store.GetByLocalIdAsync(recipe.LocalId);

            Assert.NotNull(saved);
            Assert.True(saved.CoverImageChanged);
            Assert.Equal("image/jpeg", saved.CoverImageContentType);
            Assert.Equal(image, await store.ReadCoverImageAsync(saved));

            await store.ClearCoverImageAsync(recipe.LocalId);
            saved = await store.GetByLocalIdAsync(recipe.LocalId);
            Assert.NotNull(saved);
            Assert.Null(saved.CoverImagePath);
            Assert.True(saved.CoverImageChanged);
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
    public void ConflictResolver_PushesOnlyNewerLocalEdits()
    {
        var remoteUpdatedAt = DateTime.UtcNow;
        var localRecipe = new StoredRecipe
        {
            IsDirty = true,
            UpdatedAtUtc = remoteUpdatedAt.AddSeconds(1)
        };

        Assert.True(RecipeConflictResolver.ShouldPushLocalChanges(localRecipe, remoteUpdatedAt));
        Assert.False(RecipeConflictResolver.ShouldPushLocalChanges(
            localRecipe,
            remoteUpdatedAt.AddSeconds(2)));
        localRecipe.IsDirty = false;
        Assert.False(RecipeConflictResolver.ShouldPushLocalChanges(localRecipe, remoteUpdatedAt));
    }
}
