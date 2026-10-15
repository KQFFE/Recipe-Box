using RecipeBox.Mobile.Core;

namespace RecipeBox.Tests;

public sealed class ProfileSyncServiceTests
{
    private static readonly Uri ServerAddress = new("https://recipes.example.test/");

    [Fact]
    public async Task Sync_PromotesLocalRecipesAndDownloadsProfileRecipesAndImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var localRecipe = CreateRecipe("Device-only recipe");
            await store.SaveLocalAsync(localRecipe);

            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Profile recipe", DateTime.UtcNow.AddMinutes(-1));
            var image = new RecipeImageResponse([0xFF, 0xD8, 0xFF, 0x11], "image/jpeg");
            api.Images[serverRecipe.Id] = image;
            var sync = new ProfileSyncService(store, api);

            await sync.SyncAsync(ServerAddress);

            var localRecipes = await store.GetRecipesAsync();
            Assert.Equal(2, localRecipes.Count);
            Assert.All(localRecipes, recipe => Assert.True(recipe.IsProfileData));
            Assert.All(localRecipes, recipe => Assert.NotNull(recipe.ServerId));
            var downloaded = Assert.Single(localRecipes, recipe => recipe.ServerId == serverRecipe.Id);
            Assert.Equal(serverRecipe.Title, downloaded.Title);
            Assert.Equal(image.Bytes, await store.ReadCoverImageAsync(downloaded));
            Assert.Equal(2, api.Recipes.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_KeepsDeviceOnlyRecipeWhenProfileUploadFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var localRecipe = CreateRecipe("Device-only recipe");
            await store.SaveLocalAsync(localRecipe);

            var api = new FakeRecipeProfileApi
            {
                CreateFailure = new HttpRequestException(
                    "The profile server rejected the request.",
                    null,
                    System.Net.HttpStatusCode.Unauthorized)
            };
            var sync = new ProfileSyncService(store, api);

            await Assert.ThrowsAsync<HttpRequestException>(() => sync.SyncAsync(ServerAddress));
            await store.RemoveProfileRecipesAsync();

            var savedRecipe = Assert.Single(await store.GetRecipesAsync());
            Assert.Equal(localRecipe.LocalId, savedRecipe.LocalId);
            Assert.False(savedRecipe.IsProfileData);
            Assert.Null(savedRecipe.ServerId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_UsesLastEditWinsForRecipeContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Server version", DateTime.UtcNow.AddMinutes(-5));
            var localRecipe = CreateRecipe("Local version");
            localRecipe.ServerId = serverRecipe.Id;
            localRecipe.IsProfileData = true;
            localRecipe.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await store.SaveRemoteAsync(localRecipe);
            localRecipe.Title = "Newer local edit";
            await store.SaveLocalAsync(localRecipe);

            await new ProfileSyncService(store, api).SyncAsync(ServerAddress);

            Assert.Equal("Newer local edit", api.Recipes[serverRecipe.Id].Title);
            Assert.Equal(1, api.UpdateCount);
            Assert.Equal("Newer local edit", (await store.GetByServerIdAsync(serverRecipe.Id))?.Title);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_UsesNewerServerVersionWhenItWinsConflict()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Newer server edit", DateTime.UtcNow.AddHours(1));
            var localRecipe = CreateRecipe("Older local edit");
            localRecipe.ServerId = serverRecipe.Id;
            localRecipe.IsProfileData = true;
            await store.SaveRemoteAsync(localRecipe);
            localRecipe.Title = "Older local edit";
            await store.SaveLocalAsync(localRecipe);

            await new ProfileSyncService(store, api).SyncAsync(ServerAddress);

            Assert.Equal("Newer server edit", (await store.GetByServerIdAsync(serverRecipe.Id))?.Title);
            Assert.Equal(0, api.UpdateCount);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_UsesLastEditWinsForCoverImages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Recipe with image", DateTime.UtcNow.AddMinutes(-5));
            var localRecipe = CreateRecipe(serverRecipe.Title);
            localRecipe.ServerId = serverRecipe.Id;
            localRecipe.IsProfileData = true;
            await store.SaveRemoteAsync(localRecipe);
            var localImage = new byte[] { 0xFF, 0xD8, 0xFF, 0x21 };
            await store.SaveCoverImageAsync(localRecipe.LocalId, localImage, "image/jpeg");

            await new ProfileSyncService(store, api).SyncAsync(ServerAddress);

            Assert.Equal(localImage, api.Images[serverRecipe.Id].Bytes);
            var saved = await store.GetByServerIdAsync(serverRecipe.Id);
            Assert.NotNull(saved);
            Assert.False(saved.CoverImageChanged);
            Assert.Equal(localImage, await store.ReadCoverImageAsync(saved));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_RestoresRecipeWhenServerEditIsNewerThanOfflineDeletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Newer server edit", DateTime.UtcNow.AddHours(1));
            var localRecipe = CreateRecipe("Recipe marked deleted");
            localRecipe.ServerId = serverRecipe.Id;
            localRecipe.IsProfileData = true;
            await store.SaveRemoteAsync(localRecipe);
            await store.DeleteAsync(localRecipe.LocalId);

            await new ProfileSyncService(store, api).SyncAsync(ServerAddress);

            var restored = Assert.Single(await store.GetRecipesAsync());
            Assert.Equal("Newer server edit", restored.Title);
            Assert.Contains(serverRecipe.Id, api.Recipes.Keys);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Sync_DeletesProfileRecipeWhenOfflineDeletionIsNewer()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mobile-sync-{Guid.NewGuid():N}");
        try
        {
            await using var store = CreateStore(directory);
            var api = new FakeRecipeProfileApi();
            var serverRecipe = api.AddServerRecipe("Recipe to delete", DateTime.UtcNow.AddMinutes(-5));
            var localRecipe = CreateRecipe(serverRecipe.Title);
            localRecipe.ServerId = serverRecipe.Id;
            localRecipe.IsProfileData = true;
            await store.SaveRemoteAsync(localRecipe);
            await store.DeleteAsync(localRecipe.LocalId);

            await new ProfileSyncService(store, api).SyncAsync(ServerAddress);

            Assert.Empty(await store.GetRecipesAsync());
            Assert.DoesNotContain(serverRecipe.Id, api.Recipes.Keys);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static LocalRecipeStore CreateStore(string directory) =>
        new(Path.Combine(directory, "recipes.db"), Path.Combine(directory, "images"));

    private static StoredRecipe CreateRecipe(string title) =>
        new()
        {
            Title = title,
            LanguageCode = "en",
            Ingredients = ["flour"],
            Instructions = ["Bake"]
        };

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeRecipeProfileApi : IRecipeProfileApi
    {
        private int nextId = 1;

        public Dictionary<int, RecipeResponse> Recipes { get; } = [];

        public Dictionary<int, RecipeImageResponse> Images { get; } = [];

        public int UpdateCount { get; private set; }

        public Exception? CreateFailure { get; init; }

        public RecipeResponse AddServerRecipe(string title, DateTime updatedAtUtc)
        {
            var recipe = new RecipeResponse(
                nextId++,
                title,
                null,
                "en",
                ["flour"],
                ["Bake"],
                updatedAtUtc.AddDays(-1),
                updatedAtUtc);
            Recipes.Add(recipe.Id, recipe);
            return recipe;
        }

        public Task<RecipeListResponse> GetRecipesAsync(
            Uri serverAddress,
            int page,
            CancellationToken cancellationToken = default)
        {
            var allRecipes = Recipes.Values.OrderBy(recipe => recipe.Id).ToArray();
            const int pageSize = 20;
            var items = allRecipes.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            return Task.FromResult(new RecipeListResponse(items, page, pageSize, allRecipes.Length));
        }

        public Task<RecipeResponse> GetRecipeAsync(
            Uri serverAddress,
            int recipeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Recipes[recipeId]);

        public Task<RecipeResponse> CreateRecipeAsync(
            Uri serverAddress,
            RecipeWriteRequest recipe,
            CancellationToken cancellationToken = default)
        {
            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            var now = DateTime.UtcNow;
            var created = new RecipeResponse(
                nextId++,
                recipe.Title,
                recipe.Description,
                recipe.LanguageCode,
                recipe.Ingredients,
                recipe.Instructions,
                now,
                now);
            Recipes.Add(created.Id, created);
            return Task.FromResult(created);
        }

        public Task<RecipeResponse> UpdateRecipeAsync(
            Uri serverAddress,
            int recipeId,
            RecipeWriteRequest recipe,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            var current = Recipes[recipeId];
            var updated = current with
            {
                Title = recipe.Title,
                Description = recipe.Description,
                LanguageCode = recipe.LanguageCode,
                Ingredients = recipe.Ingredients,
                Instructions = recipe.Instructions,
                UpdatedAtUtc = DateTime.UtcNow
            };
            Recipes[recipeId] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteRecipeAsync(
            Uri serverAddress,
            int recipeId,
            CancellationToken cancellationToken = default)
        {
            Recipes.Remove(recipeId);
            Images.Remove(recipeId);
            return Task.CompletedTask;
        }

        public Task<RecipeImageResponse?> GetRecipeImageAsync(
            Uri serverAddress,
            int recipeId,
            CancellationToken cancellationToken = default)
        {
            Images.TryGetValue(recipeId, out var image);
            return Task.FromResult(image);
        }

        public Task UploadRecipeImageAsync(
            Uri serverAddress,
            int recipeId,
            byte[] imageBytes,
            string contentType,
            string fileName,
            CancellationToken cancellationToken = default)
        {
            Images[recipeId] = new RecipeImageResponse(imageBytes, contentType);
            var recipe = Recipes[recipeId];
            Recipes[recipeId] = recipe with { UpdatedAtUtc = DateTime.UtcNow };
            return Task.CompletedTask;
        }

        public Task DeleteRecipeImageAsync(
            Uri serverAddress,
            int recipeId,
            CancellationToken cancellationToken = default)
        {
            Images.Remove(recipeId);
            var recipe = Recipes[recipeId];
            Recipes[recipeId] = recipe with { UpdatedAtUtc = DateTime.UtcNow };
            return Task.CompletedTask;
        }
    }
}
