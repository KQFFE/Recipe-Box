using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace RecipeBox.Web.Models;

public static class RecipePageTextParser
{
    private static readonly HashSet<string> IngredientHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "ingredients", "ingredient list", "ingredienser", "ingredienslista"
    };

    private static readonly HashSet<string> InstructionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "instructions", "directions", "method", "preparation", "steps", "how to prepare",
        "instruktioner", "tillagning", "gör så här", "gör såhär", "så här gör du", "så gör du",
        "tillvägagångssätt"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "article", "blockquote", "dd", "div", "dl", "dt", "figcaption", "figure", "h1", "h2",
        "h3", "h4", "h5", "h6", "li", "main", "ol", "p", "section", "table", "tbody", "td",
        "th", "thead", "tr", "ul"
    };

    public static RecipeDraft? Parse(string html, Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(sourceUri);

        var document = new HtmlDocument();
        document.LoadHtml(html);
        RemoveNonContentNodes(document);

        var title = GetNodeText(document.DocumentNode.SelectSingleNode("//h1"))
            ?? GetNodeText(document.DocumentNode.SelectSingleNode("//title"));
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var visibleText = new StringBuilder();
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        AppendVisibleText(body, visibleText);

        var ingredients = new List<string>();
        var instructions = new List<string>();
        var section = RecipeSection.None;
        foreach (var line in WebUtility.HtmlDecode(visibleText.ToString())
                     .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryGetSection(line, out var detectedSection, out var inlineContent))
            {
                section = detectedSection;
                AddSectionContent(section, inlineContent, ingredients, instructions);
                continue;
            }

            AddSectionContent(section, line, ingredients, instructions);
        }

        if (ingredients.Count == 0 || instructions.Count == 0)
        {
            return null;
        }

        return new RecipeDraft(
            title.Trim(),
            string.Join(Environment.NewLine, ingredients),
            string.Join(Environment.NewLine, instructions),
            GetMetaContent(document, "name", "description")
                ?? GetMetaContent(document, "property", "og:description"),
            ResolveImageUrl(GetMetaContent(document, "property", "og:image")
                ?? GetMetaContent(document, "name", "twitter:image"), sourceUri),
            RecipeLanguage.DetectCode(string.Join(
                Environment.NewLine,
                new[] { title }.Concat(ingredients).Concat(instructions))));
    }

    private static void RemoveNonContentNodes(HtmlDocument document)
    {
        var excludedNodes = document.DocumentNode.SelectNodes(
            "//script|//style|//noscript|//svg|//nav|//footer|//form|//button|//aside|//template"
            + "|//*[@hidden]|//*[@aria-hidden='true']");
        if (excludedNodes is not null)
        {
            foreach (var node in excludedNodes.ToArray())
            {
                node.Remove();
            }
        }

        var hiddenByStyle = document.DocumentNode
            .Descendants()
            .Where(node => node.NodeType == HtmlNodeType.Element)
            .Where(node =>
            {
                var style = node.GetAttributeValue("style", string.Empty);
                return style.Contains("display:none", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("display: none", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("visibility: hidden", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        foreach (var node in hiddenByStyle)
        {
            node.Remove();
        }
    }

    private static void AppendVisibleText(HtmlNode node, StringBuilder text)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            text.Append(node.InnerText);
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
        {
            return;
        }

        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            text.AppendLine();
            return;
        }

        var isBlock = BlockElements.Contains(node.Name);
        if (isBlock)
        {
            text.AppendLine();
        }

        foreach (var child in node.ChildNodes)
        {
            AppendVisibleText(child, text);
        }

        if (isBlock)
        {
            text.AppendLine();
        }
    }

    private static bool TryGetSection(
        string line,
        out RecipeSection section,
        out string inlineContent)
    {
        var candidate = line.Trim().TrimEnd(':').Trim();
        if (TryMatchHeading(candidate, IngredientHeadings, out inlineContent))
        {
            section = RecipeSection.Ingredients;
            return true;
        }

        if (TryMatchHeading(candidate, InstructionHeadings, out inlineContent))
        {
            section = RecipeSection.Instructions;
            return true;
        }

        section = RecipeSection.None;
        inlineContent = string.Empty;
        return false;
    }

    private static bool TryMatchHeading(
        string candidate,
        IReadOnlySet<string> headings,
        out string inlineContent)
    {
        foreach (var heading in headings)
        {
            if (candidate.Equals(heading, StringComparison.OrdinalIgnoreCase))
            {
                inlineContent = string.Empty;
                return true;
            }

            if (candidate.StartsWith(heading, StringComparison.OrdinalIgnoreCase)
                && candidate.Length > heading.Length)
            {
                var separator = candidate[heading.Length];
                if (separator is ':' or '-' or '\u2013' or '\u2014')
                {
                    inlineContent = candidate[(heading.Length + 1)..].Trim();
                    return true;
                }

                if (separator == ' '
                    && candidate.AsSpan(heading.Length).TrimStart().StartsWith("("))
                {
                    inlineContent = string.Empty;
                    return true;
                }
            }
        }

        inlineContent = string.Empty;
        return false;
    }

    private static void AddSectionContent(
        RecipeSection section,
        string line,
        ICollection<string> ingredients,
        ICollection<string> instructions)
    {
        var content = line.Trim().TrimStart('•', '·', '●', '○', '▪', '◦', '-', '*').Trim();
        if (content.Length == 0)
        {
            return;
        }

        switch (section)
        {
            case RecipeSection.Ingredients:
                ingredients.Add(content);
                break;
            case RecipeSection.Instructions:
                instructions.Add(content);
                break;
        }
    }

    private static string? GetNodeText(HtmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(node.InnerText).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? GetMetaContent(HtmlDocument document, string attribute, string value)
    {
        var meta = document.DocumentNode.SelectNodes("//meta")
            ?.FirstOrDefault(node => node.GetAttributeValue(attribute, string.Empty)
                .Equals(value, StringComparison.OrdinalIgnoreCase));
        var content = meta?.GetAttributeValue("content", string.Empty);
        return string.IsNullOrWhiteSpace(content) ? null : WebUtility.HtmlDecode(content).Trim();
    }

    private static string? ResolveImageUrl(string? imageUrl, Uri sourceUri)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.RelativeOrAbsolute, out var imageUri))
        {
            return null;
        }

        var resolvedUri = imageUri.IsAbsoluteUri ? imageUri : new Uri(sourceUri, imageUri);
        return resolvedUri.Scheme == Uri.UriSchemeHttp || resolvedUri.Scheme == Uri.UriSchemeHttps
            ? resolvedUri.AbsoluteUri
            : null;
    }

    private enum RecipeSection
    {
        None,
        Ingredients,
        Instructions
    }
}
