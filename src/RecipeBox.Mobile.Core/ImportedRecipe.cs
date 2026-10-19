using System.Text.RegularExpressions;

namespace RecipeBox.Mobile.Core;

// A recipe read from a web page, for the user to review before it is saved on the device.
public sealed record ImportedRecipe(
    string Title,
    string Ingredients,
    string Instructions,
    string? Description = null,
    string? ImageUrl = null,
    string LanguageCode = "en")
{
    public IReadOnlyList<string> IngredientLines => ToLines(Ingredients);

    public IReadOnlyList<string> InstructionLines => ToLines(Instructions);

    private static string[] ToLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class RecipeImportFailedException(string message) : Exception(message);

// Mirrors the web app's language detection so imports pick the same language either way.
public static partial class RecipeLanguageDetector
{
    private static readonly string[] SupportedCodes = ["en", "sv"];

    private static readonly string[] SwedishIndicators =
    [
        "ingredienser", "tillagning", "gör så här", "gör såhär", "så här gör du", "så gör du",
        "och", "lägg", "ägg", "mjölk", "smör", "socker", "blanda", "stek", "grädda", "vispa", "koka"
    ];

    private static readonly string[] EnglishIndicators =
    [
        "ingredients", "instructions", "directions", "preparation", "how to prepare",
        "and", "add", "egg", "milk", "butter", "sugar", "mix", "fry", "bake", "whisk", "boil"
    ];

    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "en";
        }

        var normalized = $" {NonWordRegex().Replace(text.ToLowerInvariant(), " ").Trim()} ";
        return Score(normalized, SwedishIndicators) > Score(normalized, EnglishIndicators) ? "sv" : "en";
    }

    public static string Resolve(string? languageCode, string? recipeText)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var separator = languageCode.IndexOfAny(['-', '_']);
            var primary = (separator < 0 ? languageCode : languageCode[..separator]).Trim().ToLowerInvariant();
            if (SupportedCodes.Contains(primary))
            {
                return primary;
            }
        }

        return Detect(recipeText);
    }

    private static int Score(string text, IEnumerable<string> indicators) =>
        indicators.Sum(indicator => CountOccurrences(text, indicator));

    private static int CountOccurrences(string text, string indicator)
    {
        var count = 0;
        var searchIndex = 0;
        while ((searchIndex = text.IndexOf($" {indicator} ", searchIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            searchIndex += indicator.Length;
        }

        return count;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWordRegex();
}
