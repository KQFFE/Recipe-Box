using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

public sealed class RecipeFormModel : IValidatableObject
{
    [Required]
    [StringLength(160)]
    [Display(Name = "Recipe name")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2048)]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Upload a cover image")]
    public IFormFile? ImageUpload { get; set; }

    [Display(Name = "Remove current uploaded image")]
    public bool RemoveUploadedImage { get; set; }

    public bool HasUploadedImage { get; set; }

    public string? CurrentImageSource { get; set; }

    [StringLength(10000)]
    [Display(Name = "Ingredients")]
    public string? Ingredients { get; set; } = string.Empty;

    [StringLength(10000)]
    [Display(Name = "Instructions")]
    public string? Instructions { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RecipeContent.ToLines(Ingredients).Count == 0)
        {
            yield return new ValidationResult(
                "Enter at least one ingredient.",
                [nameof(Ingredients)]);
        }

        if (RecipeContent.ToLines(Instructions).Count == 0)
        {
            yield return new ValidationResult(
                "Enter at least one preparation step.",
                [nameof(Instructions)]);
        }

        if (!string.IsNullOrWhiteSpace(ImageUrl)
            && (!Uri.TryCreate(ImageUrl, UriKind.Absolute, out var imageUri)
                || string.IsNullOrWhiteSpace(imageUri.Host)
                || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult(
                "Enter an image URL that starts with http:// or https://.",
                [nameof(ImageUrl)]);
        }
    }
}
