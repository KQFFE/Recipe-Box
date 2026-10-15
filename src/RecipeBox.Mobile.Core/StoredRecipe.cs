using System.Text.Json;
using SQLite;

namespace RecipeBox.Mobile.Core;

[Table("Recipes")]
public sealed class StoredRecipe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [PrimaryKey]
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");

    [Indexed(Unique = true)]
    public int? ServerId { get; set; }

    [NotNull]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [NotNull]
    public string LanguageCode { get; set; } = "en";

    [NotNull]
    public string IngredientsJson { get; set; } = "[]";

    [NotNull]
    public string InstructionsJson { get; set; } = "[]";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsDirty { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsProfileData { get; set; }

    public string? CoverImagePath { get; set; }

    public string? CoverImageContentType { get; set; }

    public bool CoverImageChanged { get; set; }

    public bool RemoteImageChecked { get; set; }

    [Ignore]
    public IReadOnlyList<string> Ingredients
    {
        get => JsonSerializer.Deserialize<string[]>(IngredientsJson, JsonOptions) ?? [];
        set => IngredientsJson = JsonSerializer.Serialize(value, JsonOptions);
    }

    [Ignore]
    public IReadOnlyList<string> Instructions
    {
        get => JsonSerializer.Deserialize<string[]>(InstructionsJson, JsonOptions) ?? [];
        set => InstructionsJson = JsonSerializer.Serialize(value, JsonOptions);
    }
}

public static class RecipeConflictResolver
{
    public static bool ShouldPushLocalChanges(StoredRecipe localRecipe, DateTime remoteUpdatedAtUtc) =>
        localRecipe.IsDirty && localRecipe.UpdatedAtUtc > remoteUpdatedAtUtc;
}
