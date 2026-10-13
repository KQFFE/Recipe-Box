using System.ComponentModel.DataAnnotations;

namespace RecipeManager.Web.Pages.Recipes;

public sealed class RecipeFormModel
{
    [Required]
    [StringLength(160)]
    [Display(Name = "Recipe name")]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Image URL")]
    public string? ImageUrl { get; set; }

    [Required]
    [Display(Name = "Ingredients")]
    public string Ingredients { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Instructions")]
    public string Instructions { get; set; } = string.Empty;
}
