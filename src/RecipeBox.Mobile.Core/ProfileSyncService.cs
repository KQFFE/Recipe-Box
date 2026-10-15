namespace RecipeBox.Mobile.Core;

public sealed class ProfileSyncService(
    LocalRecipeStore recipeStore,
    IRecipeProfileApi apiClient)
{
    private readonly SemaphoreSlim syncLock = new(1, 1);

    public async Task SyncAsync(Uri serverAddress, CancellationToken cancellationToken = default)
    {
        await syncLock.WaitAsync(cancellationToken);
        try
        {
            var serverRecipes = await GetAllServerRecipesAsync(serverAddress, cancellationToken);
            var serverById = serverRecipes.ToDictionary(recipe => recipe.Id);
            var localRecipes = await recipeStore.GetAllAsync(cancellationToken);
            var deletedServerIds = new HashSet<int>();

            foreach (var localRecipe in localRecipes.Where(recipe => !recipe.IsProfileData))
            {
                await recipeStore.MarkAsProfileDataAsync(localRecipe.LocalId, cancellationToken);
                localRecipe.IsProfileData = true;
            }

            foreach (var deletedRecipe in localRecipes.Where(recipe => recipe.IsDeleted))
            {
                if (deletedRecipe.ServerId is int serverId && serverById.ContainsKey(serverId))
                {
                    var serverRecipe = serverById[serverId];
                    if (deletedRecipe.UpdatedAtUtc > serverRecipe.UpdatedAtUtc)
                    {
                        await apiClient.DeleteRecipeAsync(serverAddress, serverId, cancellationToken);
                        deletedServerIds.Add(serverId);
                        await recipeStore.RemoveAsync(deletedRecipe, cancellationToken);
                    }
                    else
                    {
                        var restored = ToLocalRecipe(serverRecipe, deletedRecipe.LocalId);
                        await recipeStore.SaveRemoteAsync(restored, cancellationToken);
                        deletedRecipe.IsDeleted = false;
                    }
                }
                else
                {
                    await recipeStore.RemoveAsync(deletedRecipe, cancellationToken);
                }
            }

            foreach (var localRecipe in localRecipes.Where(recipe => !recipe.IsDeleted))
            {
                if (localRecipe.ServerId is null)
                {
                    await CreateServerRecipeAsync(localRecipe, serverAddress, cancellationToken);
                }
                else if (!serverById.ContainsKey(localRecipe.ServerId.Value) && localRecipe.IsDirty)
                {
                    await CreateServerRecipeAsync(localRecipe, serverAddress, cancellationToken);
                }
                else if (!serverById.ContainsKey(localRecipe.ServerId.Value))
                {
                    await recipeStore.RemoveAsync(localRecipe, cancellationToken);
                }
            }

            foreach (var serverRecipe in serverRecipes.Where(recipe => !deletedServerIds.Contains(recipe.Id)))
            {
                var localRecipe = await recipeStore.GetByServerIdAsync(serverRecipe.Id, cancellationToken);
                if (localRecipe is null)
                {
                    localRecipe = ToLocalRecipe(serverRecipe, Guid.NewGuid().ToString("N"));
                    await recipeStore.SaveRemoteAsync(localRecipe, cancellationToken);
                    var coverUpdatedAt = await SyncCoverImageAsync(localRecipe, serverAddress, cancellationToken);
                    await recipeStore.MarkSyncedAsync(
                        localRecipe.LocalId,
                        serverRecipe.Id,
                        coverUpdatedAt ?? serverRecipe.UpdatedAtUtc,
                        cancellationToken);
                    continue;
                }

                if (RecipeConflictResolver.ShouldPushLocalChanges(localRecipe, serverRecipe.UpdatedAtUtc))
                {
                    var updated = await apiClient.UpdateRecipeAsync(
                        serverAddress,
                        serverRecipe.Id,
                        ToWriteRequest(localRecipe),
                        cancellationToken);
                    await ApplyServerContentAsync(localRecipe, updated, cancellationToken);
                    var coverUpdatedAt = await SyncCoverImageAsync(localRecipe, serverAddress, cancellationToken);
                    await recipeStore.MarkSyncedAsync(
                        localRecipe.LocalId,
                        updated.Id,
                        coverUpdatedAt ?? updated.UpdatedAtUtc,
                        cancellationToken);
                }
                else
                {
                    var serverVersion = ToLocalRecipe(serverRecipe, localRecipe.LocalId);
                    serverVersion.CoverImagePath = localRecipe.CoverImagePath;
                    serverVersion.CoverImageContentType = localRecipe.CoverImageContentType;
                    serverVersion.RemoteImageChecked = localRecipe.RemoteImageChecked;
                    serverVersion.CoverImageChanged = localRecipe.CoverImageChanged
                        && localRecipe.UpdatedAtUtc > serverRecipe.UpdatedAtUtc;
                    await recipeStore.SaveRemoteAsync(serverVersion, cancellationToken);
                    await recipeStore.MarkContentSyncedAsync(
                        serverVersion.LocalId,
                        serverRecipe.Id,
                        serverRecipe.UpdatedAtUtc,
                        cancellationToken);
                    var coverUpdatedAt = await SyncCoverImageAsync(serverVersion, serverAddress, cancellationToken);
                    await recipeStore.MarkSyncedAsync(
                        serverVersion.LocalId,
                        serverRecipe.Id,
                        coverUpdatedAt ?? serverRecipe.UpdatedAtUtc,
                        cancellationToken);
                }
            }
        }
        finally
        {
            syncLock.Release();
        }
    }

    private async Task CreateServerRecipeAsync(
        StoredRecipe localRecipe,
        Uri serverAddress,
        CancellationToken cancellationToken)
    {
        var created = await apiClient.CreateRecipeAsync(
            serverAddress,
            ToWriteRequest(localRecipe),
            cancellationToken);
        await ApplyServerContentAsync(localRecipe, created, cancellationToken);
        var coverUpdatedAt = await SyncCoverImageAsync(localRecipe, serverAddress, cancellationToken);
        await recipeStore.MarkSyncedAsync(
            localRecipe.LocalId,
            created.Id,
            coverUpdatedAt ?? created.UpdatedAtUtc,
            cancellationToken);
    }

    private async Task<DateTime?> SyncCoverImageAsync(
        StoredRecipe recipe,
        Uri serverAddress,
        CancellationToken cancellationToken)
    {
        if (recipe.ServerId is not int serverId)
        {
            throw new InvalidOperationException("A recipe must be saved to the profile before syncing its image.");
        }

        if (recipe.CoverImageChanged)
        {
            if (recipe.CoverImagePath is null)
            {
                await apiClient.DeleteRecipeImageAsync(serverAddress, serverId, cancellationToken);
            }
            else
            {
                var imageBytes = await recipeStore.ReadCoverImageAsync(recipe, cancellationToken)
                    ?? throw new IOException("The saved cover image could not be read from this device.");
                var contentType = recipe.CoverImageContentType
                    ?? throw new InvalidDataException("The saved cover image type is missing.");
                await apiClient.UploadRecipeImageAsync(
                    serverAddress,
                    serverId,
                    imageBytes,
                    contentType,
                    Path.GetFileName(recipe.CoverImagePath),
                    cancellationToken);
            }

            recipe.CoverImageChanged = false;
            recipe.RemoteImageChecked = true;
            await recipeStore.SaveRemoteAsync(recipe, cancellationToken);
            return (await apiClient.GetRecipeAsync(serverAddress, serverId, cancellationToken)).UpdatedAtUtc;
        }

        var image = await apiClient.GetRecipeImageAsync(serverAddress, serverId, cancellationToken);
        await recipeStore.SaveRemoteCoverImageAsync(
            recipe.LocalId,
            image?.Bytes,
            image?.ContentType,
            cancellationToken);
        return null;
    }

    private async Task ApplyServerContentAsync(
        StoredRecipe localRecipe,
        RecipeResponse serverRecipe,
        CancellationToken cancellationToken)
    {
        localRecipe.Title = serverRecipe.Title;
        localRecipe.Description = serverRecipe.Description;
        localRecipe.LanguageCode = serverRecipe.LanguageCode;
        localRecipe.Ingredients = serverRecipe.Ingredients;
        localRecipe.Instructions = serverRecipe.Instructions;
        localRecipe.CreatedAtUtc = serverRecipe.CreatedAtUtc;
        localRecipe.ServerId = serverRecipe.Id;
        localRecipe.UpdatedAtUtc = serverRecipe.UpdatedAtUtc;
        await recipeStore.MarkContentSyncedAsync(
            localRecipe.LocalId,
            serverRecipe.Id,
            serverRecipe.UpdatedAtUtc,
            cancellationToken);
    }

    private static StoredRecipe ToLocalRecipe(RecipeResponse recipe, string localId) =>
        new()
        {
            LocalId = localId,
            ServerId = recipe.Id,
            Title = recipe.Title,
            Description = recipe.Description,
            LanguageCode = recipe.LanguageCode,
            Ingredients = recipe.Ingredients,
            Instructions = recipe.Instructions,
            CreatedAtUtc = recipe.CreatedAtUtc,
            UpdatedAtUtc = recipe.UpdatedAtUtc,
            IsProfileData = true
        };

    private static RecipeWriteRequest ToWriteRequest(StoredRecipe recipe) =>
        new(
            recipe.Title,
            recipe.Description,
            recipe.LanguageCode,
            recipe.Ingredients,
            recipe.Instructions);

    private async Task<IReadOnlyList<RecipeResponse>> GetAllServerRecipesAsync(
        Uri serverAddress,
        CancellationToken cancellationToken)
    {
        var allRecipes = new List<RecipeResponse>();
        var page = 1;
        while (true)
        {
            var result = await apiClient.GetRecipesAsync(serverAddress, page, cancellationToken);
            allRecipes.AddRange(result.Items);
            if (result.Items.Count == 0 || page * result.PageSize >= result.TotalCount)
            {
                return allRecipes;
            }

            page++;
        }
    }
}
