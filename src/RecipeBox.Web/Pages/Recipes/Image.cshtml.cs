using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[AllowAnonymous]
public sealed class ImageModel(
    ApplicationDbContext db,
    IRecipeImageStorage imageStorage) : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public async Task<IActionResult> OnGetAsync(
        int id,
        string? token,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var recipe = await db.Recipes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.StoredImageKey != null,
                cancellationToken);
        if (recipe is null
            || recipe.StoredImageKey is null
            || recipe.StoredImageContentType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            return NotFound();
        }

        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isOwner = User.Identity?.IsAuthenticated == true
            && string.Equals(ownerId, recipe.OwnerId, StringComparison.Ordinal);
        if (!isOwner && !await HasActiveShareAsync(recipe.Id, recipe.OwnerId, token, cancellationToken))
        {
            return NotFound();
        }

        var stream = await imageStorage.OpenReadAsync(
            recipe.OwnerId,
            recipe.StoredImageKey,
            cancellationToken);
        return stream is null
            ? NotFound()
            : File(stream, recipe.StoredImageContentType);
    }

    private async Task<bool> HasActiveShareAsync(
        int recipeId,
        string ownerId,
        string? token,
        CancellationToken cancellationToken)
    {
        if (!RecipeShareToken.TryGetHash(token, out var tokenHash))
        {
            return false;
        }

        return await db.RecipeShares
            .AsNoTracking()
            .AnyAsync(
                share => share.RecipeId == recipeId
                    && share.OwnerId == ownerId
                    && share.TokenHash == tokenHash
                    && share.RevokedAtUtc == null,
                cancellationToken);
    }
}
