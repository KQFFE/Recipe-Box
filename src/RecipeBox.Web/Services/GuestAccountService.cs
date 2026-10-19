using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;

namespace RecipeBox.Web.Services;

// Visitors without an account get a hidden, password-less identity so their recipes persist in this browser.
// When they sign in to a real account, the recipes move to it and the guest identity is removed.
public sealed class GuestAccountService(
    ApplicationDbContext db,
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IRecipeImageStorage imageStorage,
    ILogger<GuestAccountService> logger)
{
    public const string UserNamePrefix = "guest-";

    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(180);

    // Real accounts use an e-mail address as the user name, which can never match this prefix without an "@".
    public static bool IsGuestUserName(string? userName) =>
        userName is not null
        && userName.StartsWith(UserNamePrefix, StringComparison.Ordinal)
        && !userName.Contains('@');

    public static bool IsGuest(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && IsGuestUserName(user.Identity.Name);

    public async Task<ClaimsPrincipal> CreateAndSignInAsync(HttpContext httpContext)
    {
        var guest = new IdentityUser { UserName = UserNamePrefix + Guid.NewGuid().ToString("N") };
        var result = await userManager.CreateAsync(guest);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create a guest profile: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var principal = await signInManager.CreateUserPrincipalAsync(guest);
        await httpContext.SignInAsync(
            IdentityConstants.ApplicationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(CookieLifetime),
            });
        httpContext.User = principal;
        return principal;
    }

    public async Task<int> MergeAsync(string guestUserId, string targetUserId, CancellationToken cancellationToken)
    {
        if (guestUserId == targetUserId)
        {
            return 0;
        }

        var guest = await userManager.FindByIdAsync(guestUserId);
        if (guest is null || !IsGuestUserName(guest.UserName))
        {
            return 0;
        }

        var recipes = await db.Recipes
            .Where(recipe => recipe.OwnerId == guestUserId)
            .ToListAsync(cancellationToken);

        foreach (var recipe in recipes)
        {
            await MoveImageAsync(recipe, guestUserId, targetUserId, cancellationToken);
            recipe.OwnerId = targetUserId;
        }

        var layouts = await db.RecipeLayouts
            .Where(layout => layout.OwnerId == guestUserId)
            .ToListAsync(cancellationToken);
        foreach (var layout in layouts)
        {
            // The owner is part of the key, so the row is replaced rather than updated.
            db.RecipeLayouts.Remove(layout);
            db.RecipeLayouts.Add(new RecipeLayoutPreference
            {
                RecipeId = layout.RecipeId,
                OwnerId = targetUserId,
                SectionOrder = layout.SectionOrder,
                FullWidthSections = layout.FullWidthSections,
            });
        }

        db.RecipeShares.RemoveRange(await db.RecipeShares
            .Where(share => share.OwnerId == guestUserId)
            .ToListAsync(cancellationToken));

        await db.SaveChangesAsync(cancellationToken);
        await userManager.DeleteAsync(guest);
        return recipes.Count;
    }

    private async Task MoveImageAsync(
        Recipe recipe,
        string guestUserId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(recipe.StoredImageKey))
        {
            return;
        }

        try
        {
            await using var stream = await imageStorage.OpenReadAsync(guestUserId, recipe.StoredImageKey, cancellationToken);
            if (stream is null)
            {
                recipe.StoredImageKey = null;
                recipe.StoredImageContentType = null;
                return;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var moved = await imageStorage.SaveAsync(targetUserId, buffer.ToArray(), cancellationToken);
            var oldKey = recipe.StoredImageKey;
            recipe.StoredImageKey = moved.StorageKey;
            recipe.StoredImageContentType = moved.ContentType;
            await imageStorage.DeleteAsync(guestUserId, oldKey, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            logger.LogWarning(exception, "Could not move a guest recipe image to the new account.");
        }
    }
}
