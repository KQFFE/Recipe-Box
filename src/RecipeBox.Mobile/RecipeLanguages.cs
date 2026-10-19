namespace RecipeBox.Mobile;

public sealed record RecipeLanguage(
    string Code,
    string Name,
    string IngredientsLabel,
    string InstructionsLabel,
    string IngredientsHint,
    string InstructionsHint);

// Mirrors the language options used by the web app's recipe form.
public static class RecipeLanguages
{
    public static IReadOnlyList<RecipeLanguage> All { get; } =
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

    public static RecipeLanguage Get(string? code) =>
        All.FirstOrDefault(language => language.Code == code) ?? All[0];
}
