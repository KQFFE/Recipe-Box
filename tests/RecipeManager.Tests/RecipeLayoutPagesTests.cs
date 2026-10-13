using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Tests;

public sealed class RecipeLayoutPagesTests
{
    [Fact]
    public async Task LayoutPage_SavesOrderAndWidthWithoutChangingRecipeContent()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        var layoutPage = await client.GetAsync($"/Recipes/Layout/{recipeId}");
        Assert.Equal(HttpStatusCode.OK, layoutPage.StatusCode);
        var layoutHtml = await layoutPage.Content.ReadAsStringAsync();
        Assert.Contains("Current layout preview", layoutHtml, StringComparison.Ordinal);
        Assert.Contains("data-layout-section=\"ingredients\"", layoutHtml, StringComparison.Ordinal);
        Assert.Contains("1 cup flour", layoutHtml, StringComparison.Ordinal);
        var token = ExtractAntiforgeryToken(layoutHtml);

        var saveResponse = await client.PostAsync(
            $"/Recipes/Layout/{recipeId}?handler=Save",
            Form(token,
            [
                new("SectionOrder", "instructions"),
                new("SectionOrder", "ingredients"),
                new("SectionOrder", "description"),
                new("SectionOrder", "image"),
                new("FullWidthSections", "instructions"),
                new("FullWidthSections", "image")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var detailsHtml = await client.GetStringAsync($"/Recipes/Details/{recipeId}");
        Assert.True(
            detailsHtml.IndexOf("data-layout-section=\"instructions\"", StringComparison.Ordinal)
            < detailsHtml.IndexOf("data-layout-section=\"ingredients\"", StringComparison.Ordinal));
        Assert.Matches(
            "id=\"recipe-section-instructions\"[^>]*class=\"recipe-layout-panel recipe-layout-panel-full\"",
            detailsHtml);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var preference = await db.RecipeLayouts.SingleAsync();
        Assert.Equal("instructions|ingredients|description|image", preference.SectionOrder);
        Assert.Equal("image|instructions", preference.FullWidthSections);
        var recipe = await db.Recipes.SingleAsync();
        Assert.Equal("A description.", recipe.Description);
        Assert.Equal("1 cup flour", recipe.Ingredients);
        Assert.Equal("Mix and bake.", recipe.Instructions);
    }

    [Fact]
    public async Task LayoutPage_RejectsInvalidOrderWithoutSavingPreference()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        var layoutPage = await client.GetAsync($"/Recipes/Layout/{recipeId}");
        var token = ExtractAntiforgeryToken(await layoutPage.Content.ReadAsStringAsync());

        var response = await client.PostAsync(
            $"/Recipes/Layout/{recipeId}?handler=Save",
            Form(token,
            [
                new("SectionOrder", "ingredients"),
                new("SectionOrder", "instructions"),
                new("SectionOrder", "unknown")
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Arrange each available section exactly once",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.RecipeLayouts.ToListAsync());
    }

    [Fact]
    public async Task LayoutPage_MoveHandlersReorderSectionsInThePreview()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var recipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        var layoutPage = await client.GetAsync($"/Recipes/Layout/{recipeId}");
        var token = ExtractAntiforgeryToken(await layoutPage.Content.ReadAsStringAsync());
        var originalOrder = new[]
        {
            new KeyValuePair<string, string>("SectionOrder", "description"),
            new KeyValuePair<string, string>("SectionOrder", "image"),
            new KeyValuePair<string, string>("SectionOrder", "ingredients"),
            new KeyValuePair<string, string>("SectionOrder", "instructions"),
            new KeyValuePair<string, string>("FullWidthSections", "description"),
            new KeyValuePair<string, string>("FullWidthSections", "image")
        };

        var movedUp = await client.PostAsync(
            $"/Recipes/Layout/{recipeId}?handler=MoveUp&index=3",
            Form(token, originalOrder));
        Assert.Equal(HttpStatusCode.OK, movedUp.StatusCode);
        var movedUpHtml = await movedUp.Content.ReadAsStringAsync();
        Assert.True(
            movedUpHtml.IndexOf("data-layout-section=\"instructions\"", StringComparison.Ordinal)
            < movedUpHtml.IndexOf("data-layout-section=\"ingredients\"", StringComparison.Ordinal));

        var movedDown = await client.PostAsync(
            $"/Recipes/Layout/{recipeId}?handler=MoveDown&index=2",
            Form(ExtractAntiforgeryToken(movedUpHtml),
            [
                new("SectionOrder", "description"),
                new("SectionOrder", "image"),
                new("SectionOrder", "instructions"),
                new("SectionOrder", "ingredients"),
                new("FullWidthSections", "description"),
                new("FullWidthSections", "image")
            ]));
        Assert.Equal(HttpStatusCode.OK, movedDown.StatusCode);
        var movedDownHtml = await movedDown.Content.ReadAsStringAsync();
        Assert.True(
            movedDownHtml.IndexOf("data-layout-section=\"ingredients\"", StringComparison.Ordinal)
            < movedDownHtml.IndexOf("data-layout-section=\"instructions\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LayoutPage_DoesNotRevealOrChangeAnotherOwnersRecipeLayout()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var ownRecipeId = await CreateRecipeAsync(factory, RecipeAppFactory.TestUserId);
        var otherOwnerId = Guid.NewGuid().ToString("N");
        int otherRecipeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = $"layout-owner-{otherOwnerId}@example.test";
            db.Users.Add(new IdentityUser
            {
                Id = otherOwnerId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant()
            });
            var recipe = CreateRecipe(otherOwnerId);
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            otherRecipeId = recipe.Id;
        }

        var ownLayoutPage = await client.GetAsync($"/Recipes/Layout/{ownRecipeId}");
        var token = ExtractAntiforgeryToken(await ownLayoutPage.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Recipes/Layout/{otherRecipeId}")).StatusCode);
        var attemptedSave = await client.PostAsync(
            $"/Recipes/Layout/{otherRecipeId}?handler=Save",
            Form(token,
            [
                new("SectionOrder", "ingredients"),
                new("SectionOrder", "instructions"),
                new("SectionOrder", "description"),
                new("SectionOrder", "image")
            ]));

        Assert.Equal(HttpStatusCode.NotFound, attemptedSave.StatusCode);
        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verificationDb.RecipeLayouts.ToListAsync());
    }

    private static async Task<int> CreateRecipeAsync(RecipeAppFactory factory, string ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recipe = CreateRecipe(ownerId);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe.Id;
    }

    private static Recipe CreateRecipe(string ownerId) => new()
    {
        Title = "Layout test recipe",
        Description = "A description.",
        ImageUrl = "https://example.test/recipe.jpg",
        Ingredients = "1 cup flour",
        Instructions = "Mix and bake.",
        OwnerId = ownerId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FormUrlEncodedContent Form(
        string antiforgeryToken,
        IEnumerable<KeyValuePair<string, string>> fields) =>
        new(fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken)));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """<input name="__RequestVerificationToken" type="hidden" value="([^"]+)"\s*/?>""");
        Assert.True(match.Success, "The page should contain an antiforgery token.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
