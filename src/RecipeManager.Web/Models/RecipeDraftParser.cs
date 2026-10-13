namespace RecipeManager.Web.Models;

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

            if (title.Length == 0 && section == RecipeSection.None)
            {
                title = line;
                continue;
            }

            switch (section)
            {
                case RecipeSection.Ingredients:
                    ingredients.Add(line);
                    break;
                case RecipeSection.Instructions:
                    instructions.Add(line);
                    break;
                default:
                    ingredients.Add(line);
                    break;
            }
        }

        return new RecipeDraft(
            title,
            string.Join(Environment.NewLine, ingredients),
            string.Join(Environment.NewLine, instructions));
    }

    private static bool TryGetSection(string line, out RecipeSection section)
    {
        var heading = line.Trim().TrimEnd(':').Trim();
        if (heading.Equals("ingredients", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("ingredient list", StringComparison.OrdinalIgnoreCase))
        {
            section = RecipeSection.Ingredients;
            return true;
        }

        if (heading.Equals("instructions", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("directions", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("method", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("preparation", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("steps", StringComparison.OrdinalIgnoreCase)
            || heading.Equals("how to prepare", StringComparison.OrdinalIgnoreCase))
        {
            section = RecipeSection.Instructions;
            return true;
        }

        section = RecipeSection.None;
        return false;
    }

    private enum RecipeSection
    {
        None,
        Ingredients,
        Instructions
    }
}
