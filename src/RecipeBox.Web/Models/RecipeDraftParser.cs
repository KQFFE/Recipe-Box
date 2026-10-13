namespace RecipeBox.Web.Models;

public static class RecipeDraftParser
{
    public static RecipeDraft Parse(string recognizedText)
    {
        ArgumentNullException.ThrowIfNull(recognizedText);

        var title = string.Empty;
        var ingredients = new List<string>();
        var instructions = new List<string>();
        var section = RecipeSection.None;

        foreach (var line in RecipeContent.ToLines(recognizedText))
        {
            if (TryGetSection(line, out var detectedSection))
            {
                section = detectedSection;
                continue;
            }

            var contentLine = RemoveOcrListMarkers(line);
            if (title.Length == 0 && section == RecipeSection.None)
            {
                title = contentLine;
                continue;
            }

            switch (section)
            {
                case RecipeSection.Ingredients:
                    ingredients.Add(contentLine);
                    break;
                case RecipeSection.Instructions:
                    instructions.Add(contentLine);
                    break;
                default:
                    ingredients.Add(contentLine);
                    break;
            }
        }

        return new RecipeDraft(
            title,
            string.Join(Environment.NewLine, ingredients),
            string.Join(Environment.NewLine, instructions),
            LanguageCode: RecipeLanguage.DetectCode(recognizedText));
    }

    private static bool TryGetSection(string line, out RecipeSection section)
    {
        if (IsSectionHeading(line, "ingredients", "ingredient list", "ingredienser", "ingredienslista"))
        {
            section = RecipeSection.Ingredients;
            return true;
        }

        if (IsSectionHeading(
                line,
                "instructions",
                "directions",
                "method",
                "preparation",
                "steps",
                "how to prepare",
                "instruktioner",
                "tillagning",
                "gör så här",
                "gör såhär",
                "så här gör du",
                "så gör du",
                "tillvägagångssätt"))
        {
            section = RecipeSection.Instructions;
            return true;
        }

        section = RecipeSection.None;
        return false;
    }

    private static bool IsSectionHeading(string line, params string[] headings)
    {
        var candidate = line.Trim();
        return headings.Any(heading =>
            candidate.Equals(heading, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals($"{heading}:", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith($"{heading}:", StringComparison.OrdinalIgnoreCase));
    }

    private static string RemoveOcrListMarkers(string line) =>
        line.TrimStart().TrimStart('+', '•', '·', '●', '○', '▪', '◦', '©', '✓', '✔', '☑').TrimStart();

    private enum RecipeSection
    {
        None,
        Ingredients,
        Instructions
    }
}
