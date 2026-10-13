using System.ComponentModel.DataAnnotations;

namespace RecipeBox.Web.Models;

public sealed class Recipe
{
    public int Id { get; set; }

    [Required]
    [StringLength(160)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(2048)]
    public string? ImageUrl { get; set; }

    [StringLength(32)]
    public string? StoredImageKey { get; set; }

    [StringLength(32)]
    public string? StoredImageContentType { get; set; }

    [Required]
    public string Ingredients { get; set; } = string.Empty;

    [Required]
    public string Instructions { get; set; } = string.Empty;

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
