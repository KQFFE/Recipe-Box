using System.Text.RegularExpressions;
using System.Text.Json;

namespace RecipeManager.Web.Models;

public static partial class RecipeStructuredDataParser
{
    private static readonly Regex ScriptTagRegex = new(
        @"<script\b(?<attributes>[^>]*)>(?<json>.*?)</script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex TypeAttributeRegex = new(
        """\btype\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)'|(?<bare>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static RecipeDraft? Parse(string html, Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(sourceUri);

        foreach (Match script in ScriptTagRegex.Matches(html))
        {
            var typeMatch = TypeAttributeRegex.Match(script.Groups["attributes"].Value);
            if (!typeMatch.Success || !GetAttributeValue(typeMatch).Split(';', 2)[0].Trim()
                    .Equals("application/ld+json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(script.Groups["json"].Value);
                if (TryFindRecipe(document.RootElement, out var recipe))
                {
                    return CreateDraft(recipe, sourceUri);
                }
            }
            catch (JsonException)
            {
                // A page may contain more than one JSON-LD block; ignore malformed blocks and try the rest.
            }
        }

        return null;
    }

    private static bool TryFindRecipe(JsonElement element, out JsonElement recipe)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsRecipeType(element))
            {
                recipe = element;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    && TryFindRecipe(property.Value, out recipe))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindRecipe(item, out recipe))
                {
                    return true;
                }
            }
        }

        recipe = default;
        return false;
    }

    private static bool IsRecipeType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => IsRecipeTypeName(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String && IsRecipeTypeName(item.GetString())),
            _ => false
        };
    }

    private static bool IsRecipeTypeName(string? typeName) =>
        string.Equals(typeName, "Recipe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(typeName, "https://schema.org/Recipe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(typeName, "http://schema.org/Recipe", StringComparison.OrdinalIgnoreCase);

    private static RecipeDraft CreateDraft(JsonElement recipe, Uri sourceUri)
    {
        var instructions = new List<string>();
        if (recipe.TryGetProperty("recipeInstructions", out var instructionElement))
        {
            AppendInstructions(instructionElement, instructions);
        }

        var imageUrl = GetImageUrl(recipe, sourceUri);
        return new RecipeDraft(
            GetString(recipe, "name") ?? string.Empty,
            GetStringArray(recipe, "recipeIngredient"),
            string.Join(Environment.NewLine, instructions),
            GetString(recipe, "description"),
            imageUrl);
    }

    private static void AppendInstructions(JsonElement element, ICollection<string> instructions)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddNonEmpty(instructions, element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AppendInstructions(item, instructions);
                }
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    AddNonEmpty(instructions, text.GetString());
                }
                else if (element.TryGetProperty("itemListElement", out var items))
                {
                    AppendInstructions(items, instructions);
                }
                else if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    AddNonEmpty(instructions, name.GetString());
                }
                break;
        }
    }

    private static string GetStringArray(JsonElement recipe, string propertyName)
    {
        if (!recipe.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        var items = new List<string>();
        if (value.ValueKind == JsonValueKind.String)
        {
            AddNonEmpty(items, value.GetString());
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    AddNonEmpty(items, item.GetString());
                }
            }
        }

        return string.Join(Environment.NewLine, items);
    }

    private static string? GetImageUrl(JsonElement recipe, Uri sourceUri)
    {
        if (!recipe.TryGetProperty("image", out var image))
        {
            return null;
        }

        var imageValue = GetImageValue(image);
        if (!Uri.TryCreate(imageValue, UriKind.RelativeOrAbsolute, out var imageUri))
        {
            return null;
        }

        var resolvedUri = imageUri.IsAbsoluteUri ? imageUri : new Uri(sourceUri, imageUri);
        return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps
            ? resolvedUri.AbsoluteUri
            : null;
    }

    private static string? GetImageValue(JsonElement image) =>
        image.ValueKind switch
        {
            JsonValueKind.String => image.GetString(),
            JsonValueKind.Array => image.EnumerateArray().Select(GetImageValue).FirstOrDefault(value => value is not null),
            JsonValueKind.Object when image.TryGetProperty("url", out var url) => GetImageValue(url),
            _ => null
        };

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string GetAttributeValue(Match match) =>
        match.Groups["double"].Success
            ? match.Groups["double"].Value
            : match.Groups["single"].Success
                ? match.Groups["single"].Value
                : match.Groups["bare"].Value;

    private static void AddNonEmpty(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }
}
