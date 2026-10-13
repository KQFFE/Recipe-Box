using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Tests;

public sealed class RecipeSharingTests
{
    [Fact]
    public void ShareToken_IsUniqueUrlSafeAndOnlyItsHashNeedsPersistence()
    {
        var first = RecipeShareToken.Generate();
        var second = RecipeShareToken.Generate();

        Assert.Equal(43, first.Length);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain('=', first);
        Assert.True(RecipeShareToken.TryGetHash(first, out var hash));
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(first, hash);
        Assert.False(RecipeShareToken.TryGetHash("invalid token", out _));
        Assert.False(RecipeShareToken.TryGetHash(new string('a', 10_000), out _));
    }

    [Fact]
    public async Task SharePage_CreatesReadOnlyLinkWithSavedLayoutAndRevokesIt()
    {
        using var factory = new RecipeAppFactory();
        using var ownerClient = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        await SaveCustomLayoutAsync(factory, recipeId);

        var sharePage = await ownerClient.GetAsync($"/Recipes/Share/{recipeId}");
        Assert.Equal(HttpStatusCode.OK, sharePage.StatusCode);
        var createResponse = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Create",
            Form(ExtractAntiforgeryToken(await sharePage.Content.ReadAsStringAsync())));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains(
            "no-store",
            createResponse.Headers.CacheControl!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        var createHtml = await createResponse.Content.ReadAsStringAsync();
        Assert.Contains("Share link created.", createHtml);
        var shareUrl = ExtractShareUrl(createHtml);
        var token = new Uri(shareUrl).AbsolutePath.Split('/').Last();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var share = await db.RecipeShares.SingleAsync();
            Assert.Equal(RecipeShareTokenHash(token), share.TokenHash);
            Assert.DoesNotContain(token, share.TokenHash, StringComparison.Ordinal);
        }

        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");
        var sharedResponse = await anonymousClient.GetAsync(new Uri(shareUrl).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
        Assert.Equal("no-store", sharedResponse.Headers.CacheControl?.ToString());
        Assert.True(sharedResponse.Headers.TryGetValues("Referrer-Policy", out var referrerPolicies));
        Assert.Contains("no-referrer", referrerPolicies);
        var sharedHtml = await sharedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Shared recipe", sharedHtml);
        Assert.Contains("Share test recipe", sharedHtml);
        Assert.Contains("Recipe description.", sharedHtml);
        Assert.Contains("Chop vegetables.", sharedHtml);
        Assert.DoesNotContain("Customize layout", sharedHtml);
        Assert.DoesNotContain(">Edit</a>", sharedHtml);
        Assert.True(
            sharedHtml.IndexOf("data-layout-section=\"instructions\"", StringComparison.Ordinal)
            < sharedHtml.IndexOf("data-layout-section=\"ingredients\"", StringComparison.Ordinal));

        var revokeToken = ExtractAntiforgeryToken(createHtml);
        var revokeResponse = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Revoke",
            Form(revokeToken));
        Assert.Equal(HttpStatusCode.Redirect, revokeResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync(new Uri(shareUrl).PathAndQuery)).StatusCode);
    }

    [Fact]
    public async Task SharePage_RotatingLinkInvalidatesPreviousToken()
    {
        using var factory = new RecipeAppFactory();
        using var ownerClient = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);

        var page = await ownerClient.GetAsync($"/Recipes/Share/{recipeId}");
        var firstCreate = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Create",
            Form(ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync())));
        var firstHtml = await firstCreate.Content.ReadAsStringAsync();
        var firstUrl = ExtractShareUrl(firstHtml);
        var secondCreate = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Create",
            Form(ExtractAntiforgeryToken(firstHtml)));
        var secondUrl = ExtractShareUrl(await secondCreate.Content.ReadAsStringAsync());

        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync(new Uri(firstUrl).PathAndQuery)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymousClient.GetAsync(new Uri(secondUrl).PathAndQuery)).StatusCode);
    }

    [Fact]
    public async Task SharePage_DoesNotAllowSharingAnotherOwnersRecipe()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, "another-recipe-owner");
        var ownRecipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);

        var ownSharePage = await client.GetAsync($"/Recipes/Share/{ownRecipeId}");
        var token = ExtractAntiforgeryToken(await ownSharePage.Content.ReadAsStringAsync());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/Recipes/Share/{recipeId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync(
                $"/Recipes/Share/{recipeId}?handler=Create",
                Form(token))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.RecipeShares.ToListAsync());
    }

    [Fact]
    public async Task SharedRecipe_IsPublicButUnknownOrMalformedTokensAreNotFound()
    {
        using var factory = new RecipeAppFactory();
        using var authenticatedClient = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync("/Recipes/Shared/not-a-valid-token")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync($"/Recipes/Shared/{new string('a', 43)}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync($"/Recipes/Share/{recipeId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await authenticatedClient.GetAsync($"/Recipes/Share/{recipeId}")).StatusCode);
    }

    private static async Task<int> CreateRecipeAsync(RecipeAppFactory factory, string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Users.AnyAsync(user => user.Id == ownerId))
        {
            db.Users.Add(new IdentityUser
            {
                Id = ownerId,
                UserName = $"{ownerId}@example.test",
                NormalizedUserName = $"{ownerId}@example.test".ToUpperInvariant()
            });
            await db.SaveChangesAsync();
        }

        var recipe = new Recipe
        {
            Title = "Share test recipe",
            Description = "Recipe description.",
            ImageUrl = "https://example.test/share-recipe.jpg",
            Ingredients = "1 cup flour\n2 eggs",
            Instructions = "Chop vegetables.\nBake until ready.",
            OwnerId = ownerId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe.Id;
    }

    private static async Task SaveCustomLayoutAsync(RecipeAppFactory factory, int recipeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.RecipeLayouts.Add(new RecipeLayoutPreference
        {
            RecipeId = recipeId,
            OwnerId = RecipeAppFactory.TestUserId,
            SectionOrder = "instructions|ingredients|description|image",
            FullWidthSections = "image"
        });
        await db.SaveChangesAsync();
    }

    private static string RecipeShareTokenHash(string token)
    {
        Assert.True(RecipeShareToken.TryGetHash(token, out var hash));
        return hash;
    }

    private static FormUrlEncodedContent Form(string antiforgeryToken) =>
        new([new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken)]);

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """<input name="__RequestVerificationToken" type="hidden" value="([^"]+)"\s*/?>""");
        Assert.True(match.Success, "The page should contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractShareUrl(string html)
    {
        var match = Regex.Match(html, """<input id="share-link"[^>]*value="([^"]+)""");
        Assert.True(match.Success, "The page should contain the generated share link.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
