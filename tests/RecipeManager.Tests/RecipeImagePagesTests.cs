using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;
using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class RecipeImagePagesTests
{
    [Fact]
    public async Task CreatePage_StoresUploadedImagePrivatelyAndServesItToItsOwner()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var page = await client.GetAsync("/Recipes/Create");
        var image = new byte[] { 0xFF, 0xD8, 0xFF, 0x10, 0x20 };
        using var form = UploadForm(
            ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync()),
            image,
            [
                new("Input.Title", "Uploaded image recipe"),
                new("Input.Ingredients", "1 cup flour"),
                new("Input.Instructions", "Mix the flour.")
            ]);

        var createResponse = await client.PostAsync("/Recipes/Create", form);

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var recipeId = int.Parse(
            createResponse.Headers.Location!.OriginalString.Split('/').Last(),
            System.Globalization.CultureInfo.InvariantCulture);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recipe = await db.Recipes.SingleAsync(item => item.Id == recipeId);
        Assert.Null(recipe.ImageUrl);
        Assert.NotNull(recipe.StoredImageKey);
        Assert.Equal("image/jpeg", recipe.StoredImageContentType);
        Assert.Equal(1, factory.ImageStorage.Count);

        var imageResponse = await client.GetAsync($"/Recipes/Image/{recipeId}");
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/jpeg", imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", imageResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(image, await imageResponse.Content.ReadAsByteArrayAsync());

        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync($"/Recipes/Image/{recipeId}")).StatusCode);
    }

    [Fact]
    public async Task EditPage_ReplacesAndRemovesUploadedImageFiles()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var oldBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x01 };
        var oldImage = await factory.ImageStorage.SaveAsync(
            RecipeAppFactory.TestUserId,
            oldBytes,
            CancellationToken.None);
        var recipeId = await CreateRecipeAsync(
            factory,
            RecipeAppFactory.TestUserId,
            oldImage.StorageKey,
            oldImage.ContentType);

        var editPage = await client.GetAsync($"/Recipes/Edit/{recipeId}");
        var token = ExtractAntiforgeryToken(await editPage.Content.ReadAsStringAsync());
        var newBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x30 };
        using (var form = UploadForm(
                   token,
                   newBytes,
                   ValidEditFields()))
        {
            var replaceResponse = await client.PostAsync($"/Recipes/Edit/{recipeId}", form);
            Assert.Equal(HttpStatusCode.Redirect, replaceResponse.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recipe = await db.Recipes.SingleAsync(item => item.Id == recipeId);
            Assert.NotEqual(oldImage.StorageKey, recipe.StoredImageKey);
            Assert.Equal("image/png", recipe.StoredImageContentType);
            Assert.Equal("Existing description.", recipe.Description);
            Assert.Equal("1 cup flour", recipe.Ingredients);
        }

        var editAgain = await client.GetAsync($"/Recipes/Edit/{recipeId}");
        var secondToken = ExtractAntiforgeryToken(await editAgain.Content.ReadAsStringAsync());
        using var removeForm = Form(
            secondToken,
            ValidEditFields().Append(new("Input.RemoveUploadedImage", "true")));
        var removeResponse = await client.PostAsync($"/Recipes/Edit/{recipeId}", removeForm);
        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedRecipe = await verifyDb.Recipes.SingleAsync(item => item.Id == recipeId);
        Assert.Null(updatedRecipe.StoredImageKey);
        Assert.Null(updatedRecipe.StoredImageContentType);
        Assert.Equal("https://example.test/fallback.jpg", updatedRecipe.ImageUrl);
        Assert.Equal(0, factory.ImageStorage.Count);
    }

    [Fact]
    public async Task ImagePage_DeniesOtherOwnersAndAllowsReadOnlyShareUntilRevoked()
    {
        using var factory = new RecipeAppFactory();
        using var ownerClient = factory.CreateAppClient();
        var image = await factory.ImageStorage.SaveAsync(
            RecipeAppFactory.TestUserId,
            [0xFF, 0xD8, 0xFF, 0x01],
            CancellationToken.None);
        var recipeId = await CreateRecipeAsync(
            factory,
            RecipeAppFactory.TestUserId,
            image.StorageKey,
            image.ContentType);

        var otherOwnerId = $"other-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherEmail = $"{otherOwnerId}@example.test";
            db.Users.Add(new IdentityUser
            {
                Id = otherOwnerId,
                UserName = otherEmail,
                NormalizedUserName = otherEmail.ToUpperInvariant()
            });
            await db.SaveChangesAsync();
        }

        using var otherOwnerClient = factory.CreateClient();
        otherOwnerClient.DefaultRequestHeaders.Add("X-Test-UserId", otherOwnerId);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOwnerClient.GetAsync($"/Recipes/Image/{recipeId}")).StatusCode);

        var sharePage = await ownerClient.GetAsync($"/Recipes/Share/{recipeId}");
        var shareResponse = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Create",
            Form(ExtractAntiforgeryToken(await sharePage.Content.ReadAsStringAsync())));
        var shareHtml = await shareResponse.Content.ReadAsStringAsync();
        var shareUrl = ExtractValue(shareHtml, "share-link");
        using var anonymousClient = factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");
        var sharedPage = await anonymousClient.GetAsync(new Uri(shareUrl).PathAndQuery);
        var sharedHtml = await sharedPage.Content.ReadAsStringAsync();
        var imageSource = Regex.Match(
            sharedHtml,
            "<section id=\"recipe-section-image\"[^>]*>.*?<img src=\"([^\"]+)\"",
            RegexOptions.Singleline);
        Assert.True(imageSource.Success, "The shared recipe should include its owner-uploaded image.");
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymousClient.GetAsync(WebUtility.HtmlDecode(imageSource.Groups[1].Value))).StatusCode);

        var shareToken = ExtractAntiforgeryToken(shareHtml);
        var revokeResponse = await ownerClient.PostAsync(
            $"/Recipes/Share/{recipeId}?handler=Revoke",
            Form(shareToken));
        Assert.Equal(HttpStatusCode.Redirect, revokeResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymousClient.GetAsync(WebUtility.HtmlDecode(imageSource.Groups[1].Value))).StatusCode);
    }

    [Fact]
    public async Task CreatePage_RejectsUnsupportedUploadWithoutCreatingRecipe()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var page = await client.GetAsync("/Recipes/Create");
        using var form = UploadForm(
            ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync()),
            [0x00, 0x01, 0x02],
            [
                new("Input.Title", "Invalid image recipe"),
                new("Input.Ingredients", "1 cup flour"),
                new("Input.Instructions", "Mix the flour.")
            ]);

        var response = await client.PostAsync("/Recipes/Create", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Upload a valid JPEG, PNG, or WebP image.", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Recipes.ToListAsync());
        Assert.Equal(0, factory.ImageStorage.Count);
    }

    [Fact]
    public async Task DeletePage_RemovesTheAssociatedUploadedImage()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var image = await factory.ImageStorage.SaveAsync(
            RecipeAppFactory.TestUserId,
            [0xFF, 0xD8, 0xFF, 0x01],
            CancellationToken.None);
        var recipeId = await CreateRecipeAsync(
            factory,
            RecipeAppFactory.TestUserId,
            image.StorageKey,
            image.ContentType);
        var deletePage = await client.GetAsync($"/Recipes/Delete/{recipeId}");
        using var form = Form(
            ExtractAntiforgeryToken(await deletePage.Content.ReadAsStringAsync()),
            [new("Id", recipeId.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

        var response = await client.PostAsync($"/Recipes/Delete/{recipeId}", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, factory.ImageStorage.Count);
    }

    private static async Task<int> CreateRecipeAsync(
        RecipeAppFactory factory,
        string ownerId,
        string? storageKey,
        string? contentType)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!await db.Users.AnyAsync(user => user.Id == ownerId))
        {
            var email = $"{ownerId}@example.test";
            db.Users.Add(new IdentityUser
            {
                Id = ownerId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant()
            });
            await db.SaveChangesAsync();
        }

        var recipe = new Recipe
        {
            Title = "Image test recipe",
            Description = "Existing description.",
            ImageUrl = "https://example.test/fallback.jpg",
            StoredImageKey = storageKey,
            StoredImageContentType = contentType,
            Ingredients = "1 cup flour",
            Instructions = "Mix the flour.",
            OwnerId = ownerId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe.Id;
    }

    private static IEnumerable<KeyValuePair<string, string>> ValidEditFields() =>
    [
        new("Input.Title", "Image test recipe"),
        new("Input.Description", "Existing description."),
        new("Input.ImageUrl", "https://example.test/fallback.jpg"),
        new("Input.Ingredients", "1 cup flour"),
        new("Input.Instructions", "Mix the flour.")
    ];

    private static MultipartFormDataContent UploadForm(
        string antiforgeryToken,
        byte[] imageBytes,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(antiforgeryToken), "__RequestVerificationToken");
        foreach (var field in fields)
        {
            form.Add(new StringContent(field.Value), field.Key);
        }

        var image = new ByteArrayContent(imageBytes);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(image, "Input.ImageUpload", "cover.img");
        return form;
    }

    private static FormUrlEncodedContent Form(
        string antiforgeryToken,
        IEnumerable<KeyValuePair<string, string>> fields) =>
        new(fields.Append(new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken)));

    private static FormUrlEncodedContent Form(string antiforgeryToken) =>
        Form(antiforgeryToken, []);

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            """<input name="__RequestVerificationToken" type="hidden" value="([^"]+)"\s*/?>""");
        Assert.True(match.Success, "The page should contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractValue(string html, string elementId)
    {
        var match = Regex.Match(html, $"""<input id="{Regex.Escape(elementId)}"[^>]*value="([^"]+)""");
        Assert.True(match.Success, $"The page should contain input #{elementId}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
