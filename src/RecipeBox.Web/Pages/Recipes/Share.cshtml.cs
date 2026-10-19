using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[Authorize]
public sealed class ShareModel(ApplicationDbContext db) : RecipePageModel
{
    // Sharing needs an account; guests are sent to sign in and their recipes move with them.
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (IsGuest)
        {
            context.Result = RedirectToPage(
                "/Account/Login",
                new { area = "Identity", returnUrl = Request.Path + Request.QueryString });
        }

        base.OnPageHandlerExecuting(context);
    }

    public Recipe Recipe { get; private set; } = null!;

    public bool HasActiveShare { get; private set; }

    public string? ShareUrl { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        HasActiveShare = await db.RecipeShares
            .AsNoTracking()
            .AnyAsync(
                share => share.RecipeId == id
                    && share.OwnerId == CurrentUserId
                    && share.RevokedAtUtc == null,
                cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(int id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var share = await db.RecipeShares.SingleOrDefaultAsync(
            item => item.RecipeId == id && item.OwnerId == CurrentUserId,
            cancellationToken);
        var token = RecipeShareToken.Generate();
        if (!RecipeShareToken.TryGetHash(token, out var tokenHash))
        {
            throw new InvalidOperationException("A generated recipe share token was invalid.");
        }

        if (share is null)
        {
            db.RecipeShares.Add(new RecipeShare
            {
                RecipeId = id,
                OwnerId = CurrentUserId,
                TokenHash = tokenHash,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            share.TokenHash = tokenHash;
            share.CreatedAtUtc = DateTime.UtcNow;
            share.RevokedAtUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        HasActiveShare = true;
        ShareUrl = Url.Page(
            "/Recipes/Shared",
            null,
            new { token },
            Request.Scheme,
            Request.Host.Value,
            null)
            ?? throw new InvalidOperationException("Could not generate the recipe share URL.");
        StatusMessage = "Share link created. Copy it now; creating another link will invalidate this one.";
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var share = await db.RecipeShares.SingleOrDefaultAsync(
            item => item.RecipeId == id && item.OwnerId == CurrentUserId,
            cancellationToken);
        if (share is not null && share.RevokedAtUtc is null)
        {
            share.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadRecipeAsync(int id, CancellationToken cancellationToken)
    {
        var recipe = await db.Recipes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OwnerId == CurrentUserId,
                cancellationToken);
        if (recipe is null)
        {
            return false;
        }

        Recipe = recipe;
        return true;
    }
}
