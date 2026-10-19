namespace RecipeBox.Mobile.Core;

// Mirrors the web app's per-recipe section order and full-width settings.
public static class RecipeLayout
{
    public const string Description = "description";
    public const string Image = "image";
    public const string Ingredients = "ingredients";
    public const string Instructions = "instructions";

    public static IReadOnlyList<string> AvailableSections(StoredRecipe recipe, bool? hasImage = null)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            sections.Add(Description);
        }

        if (hasImage ?? recipe.CoverImagePath is not null)
        {
            sections.Add(Image);
        }

        sections.Add(Ingredients);
        sections.Add(Instructions);
        return sections;
    }

    public static RecipeLayoutSettings Resolve(StoredRecipe recipe, bool? hasImage = null)
    {
        var available = AvailableSections(recipe, hasImage);
        var stored = Split(recipe.LayoutOrder);
        if (stored is null)
        {
            return Default(available);
        }

        // Sections added after the layout was saved (for example a new image) go last; missing ones are dropped.
        var order = stored
            .Distinct(StringComparer.Ordinal)
            .Where(available.Contains)
            .Concat(available.Where(key => !stored.Contains(key)))
            .ToArray();
        var fullWidth = (Split(recipe.LayoutFullWidth) ?? [])
            .Where(order.Contains)
            .ToHashSet(StringComparer.Ordinal);
        if (!stored.Contains(Image) && order.Contains(Image))
        {
            fullWidth.Add(Image);
        }

        return new RecipeLayoutSettings(order, fullWidth);
    }

    public static RecipeLayoutSettings Default(IReadOnlyList<string> available) =>
        new(
            available,
            available.Where(key => key is Description or Image).ToHashSet(StringComparer.Ordinal));

    public static void Save(StoredRecipe recipe, IReadOnlyList<string> order, IReadOnlySet<string> fullWidth)
    {
        recipe.LayoutOrder = Join(order);
        recipe.LayoutFullWidth = Join(order.Where(fullWidth.Contains).Order(StringComparer.Ordinal).ToArray());
    }

    public static void ApplyServerLayout(
        StoredRecipe recipe,
        IReadOnlyList<string>? order,
        IReadOnlyList<string>? fullWidth)
    {
        recipe.LayoutOrder = Join(order);
        recipe.LayoutFullWidth = Join(fullWidth);
    }

    public static string? Join(IReadOnlyList<string>? keys) =>
        keys is null ? null : string.Join('|', keys);

    public static string[]? Split(string? value) =>
        value?.Split('|', StringSplitOptions.RemoveEmptyEntries);
}

public sealed record RecipeLayoutSettings(
    IReadOnlyList<string> Order,
    IReadOnlySet<string> FullWidth);
