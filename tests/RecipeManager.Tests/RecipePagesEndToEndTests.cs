using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Tests;

public sealed class RecipePagesEndToEndTests
{
    [Fact]
    public async Task RecipePages_SupportCreateReadUpdateDelete()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var listResponse = await client.GetAsync("/Recipes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var createResponse = await client.GetAsync("/Recipes/Create");
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createToken = ExtractAntiforgeryToken(await createResponse.Content.ReadAsStringAsync());

        var saveResponse = await client.PostAsync(
            "/Recipes/Create",
            Form(createToken, new Dictionary<string, string>
            {
                ["Input.Title"] = "Roasted tomato soup",
                ["Input.Description"] = "A simple weeknight soup.",
                ["Input.Ingredients"] = "6 tomatoes\n1 onion",
                ["Input.Instructions"] = "Roast the vegetables.\nBlend until smooth."
            }));
        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);

        var recipePath = Assert.IsType<string>(saveResponse.Headers.Location?.OriginalString);
        var detailsResponse = await client.GetAsync(recipePath);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var details = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Ingredients", details);
        Assert.Contains("Roast the vegetables.", details);
        Assert.Contains("Blend until smooth.", details);

        var editResponse = await client.GetAsync($"{recipePath.Replace("/Details/", "/Edit/")}");
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var editToken = ExtractAntiforgeryToken(await editResponse.Content.ReadAsStringAsync());
        var updateResponse = await client.PostAsync(
            editResponse.RequestMessage!.RequestUri!.AbsolutePath,
            Form(editToken, new Dictionary<string, string>
            {
                ["Input.Title"] = "Smoky tomato soup",
                ["Input.Description"] = "Updated recipe.",
                ["Input.Ingredients"] = "6 tomatoes\n1 onion\n1 tsp smoked paprika",
                ["Input.Instructions"] = "Roast the vegetables.\nBlend until smooth."
            }));
        Assert.Equal(HttpStatusCode.Redirect, updateResponse.StatusCode);

        var updatedDetails = await client.GetStringAsync(recipePath);
        Assert.Contains("Smoky tomato soup", updatedDetails);
        Assert.Contains("1 tsp smoked paprika", updatedDetails);

        var deletePath = recipePath.Replace("/Details/", "/Delete/");
        var deleteResponse = await client.GetAsync(deletePath);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleteToken = ExtractAntiforgeryToken(await deleteResponse.Content.ReadAsStringAsync());
        var deleteResult = await client.PostAsync(
            deletePath,
            Form(deleteToken, new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, deleteResult.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(recipePath)).StatusCode);
        var emptyList = await client.GetStringAsync("/Recipes");
        Assert.Contains("recipe box is empty", emptyList);
    }

    [Fact]
    public async Task RecipePages_RequireAuthentication()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        foreach (var path in new[] { "/Recipes", "/Recipes/Create" })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task RecipeDetails_HideRecipesOwnedByAnotherAccount()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        int recipeId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherOwnerId = Guid.NewGuid().ToString("N");
            var otherOwnerEmail = $"another-owner-{otherOwnerId}@example.test";
            db.Users.Add(new IdentityUser
            {
                Id = otherOwnerId,
                UserName = otherOwnerEmail,
                NormalizedUserName = otherOwnerEmail.ToUpperInvariant()
            });
            var recipe = new Recipe
            {
                Title = "Private recipe",
                Ingredients = "1 ingredient",
                Instructions = "1 step",
                OwnerId = otherOwnerId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            recipeId = recipe.Id;
        }

        foreach (var page in new[] { "Details", "Edit", "Delete" })
        {
            var response = await client.GetAsync($"/Recipes/{page}/{recipeId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static FormUrlEncodedContent Form(
        string antiforgeryToken,
        IDictionary<string, string> fields)
    {
        var values = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        return new FormUrlEncodedContent(values);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

        Assert.True(match.Success, "The form should contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
