using SQLite;

namespace RecipeBox.Mobile.Core;

public sealed class LocalRecipeStore : IAsyncDisposable
{
    private readonly SQLiteAsyncConnection database;
    private readonly string imageDirectory;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private bool initialized;

    public LocalRecipeStore(string databasePath, string imageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDirectory);
        database = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        this.imageDirectory = imageDirectory;
    }

    public async Task<IReadOnlyList<StoredRecipe>> GetRecipesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var recipes = await database.Table<StoredRecipe>()
            .Where(recipe => !recipe.IsDeleted)
            .OrderBy(recipe => recipe.Title)
            .ToListAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return recipes;
    }

    public async Task<IReadOnlyList<StoredRecipe>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await database.Table<StoredRecipe>().ToListAsync();
    }

    public async Task<StoredRecipe?> GetByLocalIdAsync(
        string localId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await database.FindAsync<StoredRecipe>(localId);
    }

    public async Task<StoredRecipe?> GetByServerIdAsync(
        int serverId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await database.Table<StoredRecipe>()
            .Where(recipe => recipe.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    public async Task SaveLocalAsync(
        StoredRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ValidateAndNormalize(recipe);
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        recipe.IsDirty = true;
        await database.InsertOrReplaceAsync(recipe);
    }

    public async Task SaveRemoteAsync(
        StoredRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        recipe.IsDirty = false;
        recipe.IsDeleted = false;
        await database.InsertOrReplaceAsync(recipe);
    }

    public async Task MarkSyncedAsync(
        string localId,
        int serverId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        recipe.ServerId = serverId;
        recipe.UpdatedAtUtc = updatedAtUtc;
        recipe.IsDirty = false;
        recipe.IsDeleted = false;
        recipe.IsProfileData = true;
        recipe.CoverImageChanged = false;
        recipe.RemoteImageChecked = true;
        await SaveRemoteAsync(recipe, cancellationToken);
    }

    public async Task MarkContentSyncedAsync(
        string localId,
        int serverId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        recipe.ServerId = serverId;
        recipe.UpdatedAtUtc = updatedAtUtc;
        recipe.IsDirty = false;
        recipe.IsDeleted = false;
        recipe.IsProfileData = true;
        cancellationToken.ThrowIfCancellationRequested();
        await database.UpdateAsync(recipe);
    }

    public async Task DeleteAsync(
        string localId,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken);
        if (recipe is null)
        {
            return;
        }

        if (recipe.ServerId is null)
        {
            await RemoveAsync(recipe, cancellationToken);
            return;
        }

        recipe.IsDeleted = true;
        recipe.IsDirty = true;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await database.UpdateAsync(recipe);
    }

    public async Task RemoveAsync(
        StoredRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        await EnsureInitializedAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await database.DeleteAsync(recipe);
        if (recipe.CoverImagePath is not null)
        {
            File.Delete(recipe.CoverImagePath);
        }
    }

    public async Task RemoveProfileRecipesAsync(CancellationToken cancellationToken = default)
    {
        var recipes = await GetAllAsync(cancellationToken);
        foreach (var recipe in recipes.Where(recipe => recipe.IsProfileData || recipe.ServerId is not null))
        {
            await RemoveAsync(recipe, cancellationToken);
        }
    }

    public async Task MarkAsProfileDataAsync(
        string localId,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        recipe.IsProfileData = true;
        cancellationToken.ThrowIfCancellationRequested();
        await database.UpdateAsync(recipe);
    }

    public async Task SaveCoverImageAsync(
        string localId,
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("The cover image must be JPEG, PNG, or WebP.", nameof(contentType))
        };

        Directory.CreateDirectory(imageDirectory);
        var imagePath = GetImagePath(localId, extension);
        var temporaryPath = imagePath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, imageBytes, cancellationToken);
        File.Move(temporaryPath, imagePath, overwrite: true);
        var oldPath = recipe.CoverImagePath;
        recipe.CoverImagePath = imagePath;
        recipe.CoverImageContentType = contentType;
        recipe.CoverImageChanged = true;
        recipe.RemoteImageChecked = true;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await database.UpdateAsync(recipe);
        DeletePreviousImage(oldPath, imagePath);
    }

    public async Task SaveRemoteCoverImageAsync(
        string localId,
        byte[]? imageBytes,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        var oldPath = recipe.CoverImagePath;
        string? imagePath = null;
        if (imageBytes is not null)
        {
            var extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => throw new ArgumentException("The cover image must be JPEG, PNG, or WebP.", nameof(contentType))
            };
            Directory.CreateDirectory(imageDirectory);
            imagePath = GetImagePath(localId, extension);
            var temporaryPath = imagePath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, imageBytes, cancellationToken);
            File.Move(temporaryPath, imagePath, overwrite: true);
        }

        recipe.CoverImagePath = imagePath;
        recipe.CoverImageContentType = imageBytes is null ? null : contentType;
        recipe.CoverImageChanged = false;
        recipe.RemoteImageChecked = true;
        await database.UpdateAsync(recipe);
        DeletePreviousImage(oldPath, imagePath);
    }

    public async Task ClearCoverImageAsync(
        string localId,
        CancellationToken cancellationToken = default)
    {
        var recipe = await GetByLocalIdAsync(localId, cancellationToken)
            ?? throw new InvalidOperationException("The local recipe no longer exists.");
        var oldPath = recipe.CoverImagePath;
        recipe.CoverImagePath = null;
        recipe.CoverImageContentType = null;
        recipe.CoverImageChanged = true;
        recipe.RemoteImageChecked = true;
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await database.UpdateAsync(recipe);
        DeletePreviousImage(oldPath, null);
    }

    public async Task<byte[]?> ReadCoverImageAsync(
        StoredRecipe recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.CoverImagePath is null || !File.Exists(recipe.CoverImagePath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(recipe.CoverImagePath, cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            Directory.CreateDirectory(imageDirectory);
            await database.CreateTableAsync<StoredRecipe>();
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private string GetImagePath(string localId, string extension)
    {
        if (!Guid.TryParseExact(localId, "N", out _))
        {
            throw new ArgumentException("The local recipe identifier is invalid.", nameof(localId));
        }

        return Path.Combine(imageDirectory, localId + extension);
    }

    private static void DeletePreviousImage(string? oldPath, string? newPath)
    {
        if (oldPath is not null
            && !string.Equals(oldPath, newPath, StringComparison.Ordinal)
            && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }
    }

    private static void ValidateAndNormalize(StoredRecipe recipe)
    {
        recipe.Title = recipe.Title.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(recipe.Description)
            ? null
            : recipe.Description.Trim();
        recipe.Ingredients = recipe.Ingredients
            .Select(ingredient => ingredient.Trim())
            .Where(ingredient => ingredient.Length > 0)
            .ToArray();
        recipe.Instructions = recipe.Instructions
            .Select(instruction => instruction.Trim())
            .Where(instruction => instruction.Length > 0)
            .ToArray();

        if (recipe.Title.Length is 0 or > 160)
        {
            throw new InvalidOperationException("Enter a recipe title up to 160 characters long.");
        }

        if (recipe.Description?.Length > 2000)
        {
            throw new InvalidOperationException("Keep the description to 2,000 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(recipe.LanguageCode) || recipe.LanguageCode.Length > 16)
        {
            throw new InvalidOperationException("Choose a valid recipe language.");
        }

        if (recipe.Ingredients.Count == 0)
        {
            throw new InvalidOperationException("Enter at least one ingredient.");
        }

        if (recipe.Instructions.Count == 0)
        {
            throw new InvalidOperationException("Enter at least one preparation step.");
        }

        if (string.Join(Environment.NewLine, recipe.Ingredients).Length > 10000
            || string.Join(Environment.NewLine, recipe.Instructions).Length > 10000)
        {
            throw new InvalidOperationException("Keep ingredients and instructions to 10,000 characters or fewer.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await database.CloseAsync();
        initializationLock.Dispose();
    }
}
