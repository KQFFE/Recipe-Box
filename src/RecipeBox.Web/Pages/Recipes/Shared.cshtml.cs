using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[AllowAnonymous]
public sealed class SharedModel(ApplicationDbContext db) : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public RecipePresentation Presentation { get; private set; } = null!;

    public string Token { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!RecipeShareToken.TryGetHash(token, out var tokenHash))
        {
            return NotFound();
        }
        Token = token;

        var share = await db.RecipeShares
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.TokenHash == tokenHash && item.RevokedAtUtc == null,
                cancellationToken);
        if (share is null)
        {
            return NotFound();
        }

        var recipe = await db.Recipes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == share.RecipeId && item.OwnerId == share.OwnerId,
                cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        var preference = await db.RecipeLayouts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                layout => layout.RecipeId == recipe.Id && layout.OwnerId == share.OwnerId,
                cancellationToken);
        var settings = RecipeLayoutDefinition.FromPreference(recipe, preference);
        Presentation = new RecipePresentation(
            recipe,
            RecipeContent.ToLines(recipe.Ingredients),
            RecipeContent.ToLines(recipe.Instructions),
            settings.Sections,
            settings.FullWidthSections,
            !string.IsNullOrWhiteSpace(recipe.StoredImageKey)
                ? Url.Page("/Recipes/Image", values: new { id = recipe.Id, token })
                : recipe.ImageUrl);
        return Page();
    }
}
