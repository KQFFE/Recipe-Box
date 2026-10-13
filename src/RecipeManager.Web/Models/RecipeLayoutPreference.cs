using System.ComponentModel.DataAnnotations;

namespace RecipeManager.Web.Models;

public sealed class RecipeLayoutPreference
{
    public int RecipeId { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string SectionOrder { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string FullWidthSections { get; set; } = string.Empty;
}

public sealed record RecipeLayoutSection(string Key, string Label);

public sealed record RecipeLayoutSettings(
    IReadOnlyList<RecipeLayoutSection> Sections,
    IReadOnlySet<string> FullWidthSections);

public sealed record RecipePresentation(
    Recipe Recipe,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<RecipeLayoutSection> LayoutSections,
    IReadOnlySet<string> FullWidthSections,
    string? ImageSource);

public static class RecipeLayoutDefinition
{
    public const string Description = "description";
    public const string Image = "image";
    public const string Ingredients = "ingredients";
    public const string Instructions = "instructions";

    public static IReadOnlyList<RecipeLayoutSection> AvailableSections(Recipe recipe)
    {
        var sections = new List<RecipeLayoutSection>();
        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            sections.Add(new RecipeLayoutSection(Description, "Description"));
        }

        if (!string.IsNullOrWhiteSpace(recipe.ImageUrl)
            || !string.IsNullOrWhiteSpace(recipe.StoredImageKey))
        {
            sections.Add(new RecipeLayoutSection(Image, "Image"));
        }

        sections.Add(new RecipeLayoutSection(Ingredients, "Ingredients"));
        sections.Add(new RecipeLayoutSection(Instructions, "How to prepare"));
        return sections;
    }

    public static RecipeLayoutSettings Default(Recipe recipe)
    {
        var sections = AvailableSections(recipe);
        var fullWidth = sections
            .Where(section => section.Key is Description or Image)
            .Select(section => section.Key)
            .ToHashSet(StringComparer.Ordinal);
        return new RecipeLayoutSettings(sections, fullWidth);
    }

    public static RecipeLayoutSettings FromPreference(Recipe recipe, RecipeLayoutPreference? preference)
    {
        if (preference is null)
        {
            return Default(recipe);
        }

        var available = AvailableSections(recipe);
        var order = preference.SectionOrder.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var fullWidth = preference.FullWidthSections.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return TryCreate(available, order, fullWidth, out var settings)
            ? settings
            : Default(recipe);
    }

    public static bool TryCreate(
        IReadOnlyList<RecipeLayoutSection> available,
        IReadOnlyList<string>? sectionOrder,
        IReadOnlyList<string>? fullWidthSections,
        out RecipeLayoutSettings settings)
    {
        settings = new RecipeLayoutSettings([], new HashSet<string>(StringComparer.Ordinal));
        if (sectionOrder is null || fullWidthSections is null || available.Count == 0)
        {
            return false;
        }

        var availableKeys = available.Select(section => section.Key).ToHashSet(StringComparer.Ordinal);
        var orderedKeys = sectionOrder.ToHashSet(StringComparer.Ordinal);
        var wideKeys = fullWidthSections.ToHashSet(StringComparer.Ordinal);
        if (orderedKeys.Count != sectionOrder.Count
            || orderedKeys.Count != availableKeys.Count
            || !orderedKeys.SetEquals(availableKeys)
            || wideKeys.Count != fullWidthSections.Count
            || !wideKeys.IsSubsetOf(availableKeys))
        {
            return false;
        }

        var sectionsByKey = available.ToDictionary(section => section.Key, StringComparer.Ordinal);
        settings = new RecipeLayoutSettings(
            sectionOrder.Select(key => sectionsByKey[key]).ToArray(),
            wideKeys);
        return true;
    }

    public static RecipeLayoutPreference ToPreference(string ownerId, int recipeId, RecipeLayoutSettings settings) =>
        new()
        {
            OwnerId = ownerId,
            RecipeId = recipeId,
            SectionOrder = string.Join('|', settings.Sections.Select(section => section.Key)),
            FullWidthSections = string.Join('|', settings.FullWidthSections.Order(StringComparer.Ordinal))
        };
}
