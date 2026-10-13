using System.Text.RegularExpressions;

namespace RecipeBox.Web.Models;

public sealed record RecipeLanguageOption(
    string Code,
    string Name,
    string IngredientsLabel,
    string InstructionsLabel,
    string IngredientsHint,
    string InstructionsHint);

public static class RecipeLanguage
{
    private static readonly IReadOnlyList<RecipeLanguageOption> SupportedLanguages =
    [
        new(
            "en",
            "English",
            "Ingredients",
            "Instructions",
            "One ingredient per line, including quantity when known.",
            "One preparation step per line, in order."),
        new(
            "sv",
            "Svenska",
            "Ingredienser",
            "Gör så här",
            "En ingrediens per rad, ange mängd om den är känd.",
            "Ett tillagningssteg per rad, i ordning.")
    ];

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

    public static IReadOnlyList<RecipeLanguageOption> Options => SupportedLanguages;

    public static string NormalizeCode(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var separator = languageCode.IndexOfAny(['-', '_']);
            var primaryLanguage = (separator < 0 ? languageCode : languageCode[..separator])
                .Trim()
                .ToLowerInvariant();
            if (SupportedLanguages.Any(language => language.Code == primaryLanguage))
            {
                return primaryLanguage;
            }
        }

        return EnglishCode;
    }

    public const string EnglishCode = "en";

    public static string DetectCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return EnglishCode;
        }

        var normalizedText = $" {Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim()} ";
        var swedishScore = Score(normalizedText, SwedishIndicators);
        var englishScore = Score(normalizedText, EnglishIndicators);
        return swedishScore > englishScore ? "sv" : EnglishCode;
    }

    public static string ResolveCode(string? languageCode, string? recipeText)
    {
        if (TryNormalizeSupportedCode(languageCode, out var normalizedCode))
        {
            return normalizedCode;
        }

        return DetectCode(recipeText);
    }

    public static string IngredientsLabel(string? languageCode) =>
        GetOption(languageCode).IngredientsLabel;

    public static string InstructionsLabel(string? languageCode) =>
        GetOption(languageCode).InstructionsLabel;

    public static string IngredientsHint(string? languageCode) =>
        GetOption(languageCode).IngredientsHint;

    public static string InstructionsHint(string? languageCode) =>
        GetOption(languageCode).InstructionsHint;

    public static string SectionLabel(string key, string? languageCode)
    {
        var language = GetOption(languageCode);
        return key switch
        {
            RecipeLayoutDefinition.Description => language.Code == "sv" ? "Beskrivning" : "Description",
            RecipeLayoutDefinition.Image => language.Code == "sv" ? "Bild" : "Image",
            RecipeLayoutDefinition.Ingredients => language.IngredientsLabel,
            RecipeLayoutDefinition.Instructions => language.InstructionsLabel,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown recipe section.")
        };
    }

    private static bool TryNormalizeSupportedCode(string? languageCode, out string normalizedCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var separator = languageCode.IndexOfAny(['-', '_']);
            var primaryLanguage = (separator < 0 ? languageCode : languageCode[..separator])
                .Trim()
                .ToLowerInvariant();
            var supported = SupportedLanguages.FirstOrDefault(language => language.Code == primaryLanguage);
            if (supported is not null)
            {
                normalizedCode = supported.Code;
                return true;
            }
        }

        normalizedCode = EnglishCode;
        return false;
    }

    private static RecipeLanguageOption GetOption(string? languageCode)
    {
        var code = NormalizeCode(languageCode);
        return SupportedLanguages.Single(language => language.Code == code);
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
}
