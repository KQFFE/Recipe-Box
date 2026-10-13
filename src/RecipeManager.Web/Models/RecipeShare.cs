using System.ComponentModel.DataAnnotations;

namespace RecipeManager.Web.Models;

public sealed class RecipeShare
{
    public int RecipeId { get; set; }

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}
