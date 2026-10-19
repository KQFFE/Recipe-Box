using System.ComponentModel.DataAnnotations;
using RecipeBox.Web.Models;
using RecipeBox.Web.Pages.Recipes;

namespace RecipeBox.Web.Api;

public sealed class RecipeRequest
{
    [Required]
    [StringLength(160)]
    public string Title { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Required]
    [StringLength(16)]
    public string LanguageCode { get; init; } = RecipeLanguage.EnglishCode;

    [Required]
    public IReadOnlyList<string>? Ingredients { get; init; }

    [Required]
    public IReadOnlyList<string>? Instructions { get; init; }

    // Both layout lists must be supplied together; omit them to leave the layout unchanged.
    public IReadOnlyList<string>? SectionOrder { get; init; }

    public IReadOnlyList<string>? FullWidthSections { get; init; }

    public bool TryGetLayout(out string sectionOrder, out string fullWidthSections)
    {
        sectionOrder = fullWidthSections = string.Empty;
        if (SectionOrder is null || FullWidthSections is null)
        {
            return false;
        }

        sectionOrder = string.Join('|', SectionOrder);
        fullWidthSections = string.Join('|', FullWidthSections.Order(StringComparer.Ordinal));
        return true;
    }

    public bool HasValidLayout()
    {
        if (SectionOrder is null && FullWidthSections is null)
        {
            return true;
        }

        if (SectionOrder is null || FullWidthSections is null)
        {
            return false;
        }

        var known = new[]
        {
            RecipeLayoutDefinition.Description,
            RecipeLayoutDefinition.Image,
            RecipeLayoutDefinition.Ingredients,
            RecipeLayoutDefinition.Instructions
        };
        return SectionOrder.Count <= known.Length
            && SectionOrder.Distinct(StringComparer.Ordinal).Count() == SectionOrder.Count
            && SectionOrder.All(key => known.Contains(key, StringComparer.Ordinal))
            && FullWidthSections.Distinct(StringComparer.Ordinal).Count() == FullWidthSections.Count
            && FullWidthSections.All(key => SectionOrder.Contains(key, StringComparer.Ordinal));
    }

    public RecipeFormModel ToValidatedForm()
    {
        var ingredients = Ingredients is null ? null : string.Join(Environment.NewLine, Ingredients);
        var instructions = Instructions is null ? null : string.Join(Environment.NewLine, Instructions);
        var form = new RecipeFormModel
        {
            Title = Title,
            Description = Description,
            LanguageCode = LanguageCode,
            Ingredients = ingredients,
            Instructions = instructions
        };
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                form,
                new ValidationContext(form),
                validationResults,
                validateAllProperties: true))
        {
            throw new RecipeRequestValidationException(validationResults);
        }

        return form;
    }
}

public sealed class RecipeRequestValidationException(
    IReadOnlyCollection<ValidationResult> validationResults) : Exception
{
    public IReadOnlyCollection<ValidationResult> ValidationResults { get; } = validationResults;
}
