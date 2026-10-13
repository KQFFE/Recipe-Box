namespace RecipeManager.Web.Models;

public static class RecipeContent
{
    public static string NormalizeLines(string content) =>
        string.Join(
            Environment.NewLine,
            content.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static IReadOnlyList<string> ToLines(string? content) =>
        string.IsNullOrWhiteSpace(content)
            ? []
            : content.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
