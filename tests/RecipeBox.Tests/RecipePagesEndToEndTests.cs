using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Tests;

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
        var invalidUpdateResponse = await client.PostAsync(
            editResponse.RequestMessage!.RequestUri!.AbsolutePath,
            Form(editToken, new Dictionary<string, string>
            {
                ["Input.Title"] = "This update should not persist",
                ["Input.Description"] = "Invalid update.",
                ["Input.Ingredients"] = "6 tomatoes",
                ["Input.Instructions"] = " "
            }));
        Assert.Equal(HttpStatusCode.OK, invalidUpdateResponse.StatusCode);
        Assert.Contains(
            "Enter at least one preparation step.",
            VisibleText(await invalidUpdateResponse.Content.ReadAsStringAsync()),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Roasted tomato soup", await client.GetStringAsync(recipePath));

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
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Recipes.Where(recipe => recipe.OwnerId == RecipeAppFactory.TestUserId).ToListAsync());
    }

    [Fact]
    public async Task RecipePages_RequireAuthentication()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        foreach (var path in new[]
                 {
                     "/Recipes",
                     "/Recipes/Create",
                     "/Recipes/Capture",
                     "/Recipes/Import",
                     "/Recipes/Layout/1",
                     "/Recipes/Share/1"
                 })
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task CreatePage_RejectsEmptyContentAndUnsafeImageUrl()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var createPage = await client.GetAsync("/Recipes/Create");
        var token = ExtractAntiforgeryToken(await createPage.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/Recipes/Create",
            Form(token, new Dictionary<string, string>
            {
                ["Input.Title"] = "Tomato soup",
                ["Input.Ingredients"] = " \r\n ",
                ["Input.Instructions"] = "Roast the tomatoes.",
                ["Input.ImageUrl"] = "javascript:alert(1)"
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            VisibleText(html).Contains("Enter at least one ingredient.", StringComparison.OrdinalIgnoreCase),
            $"Invalid post response:\n{VisibleText(html)}");
        Assert.Contains(
            "Enter an image URL that starts with http:// or https://.",
            VisibleText(html),
            StringComparison.OrdinalIgnoreCase);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Recipes.Where(recipe => recipe.OwnerId == RecipeAppFactory.TestUserId).ToListAsync());
    }

    [Fact]
    public async Task CapturePage_ExtractsReviewsAndSavesRecipeDraft()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var capturePage = await client.GetAsync("/Recipes/Capture");
        var token = ExtractAntiforgeryToken(await capturePage.Content.ReadAsStringAsync());

        using var upload = CreateImageUpload(token, [0xFF, 0xD8, 0xFF, 0xD9]);
        var extractResponse = await client.PostAsync("/Recipes/Capture?handler=Extract", upload);
        Assert.Equal(HttpStatusCode.OK, extractResponse.StatusCode);
        var reviewHtml = await extractResponse.Content.ReadAsStringAsync();
        Assert.Contains("Review the extracted recipe", reviewHtml);
        Assert.Contains("value=\"Tomato soup\"", reviewHtml);
        Assert.Contains("4 tomatoes", reviewHtml);
        Assert.Contains("Roast the vegetables.", reviewHtml);

        var reviewToken = ExtractAntiforgeryToken(reviewHtml);
        var saveResponse = await client.PostAsync(
            "/Recipes/Capture?handler=Save",
            Form(reviewToken, new Dictionary<string, string>
            {
                ["Input.Title"] = "Tomato soup",
                ["Input.Ingredients"] = "4 tomatoes\n1 onion",
                ["Input.Instructions"] = "Roast the vegetables.\nBlend until smooth."
            }));

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        Assert.Equal(1, factory.TextExtractor.CallCount);
        Assert.Equal(4, factory.TextExtractor.LastImageLength);

        var details = await client.GetStringAsync(saveResponse.Headers.Location!.OriginalString);
        Assert.Contains("Tomato soup", details);
        Assert.Contains("1 onion", details);
        Assert.Contains("Blend until smooth.", details);
    }

    [Fact]
    public async Task CapturePage_DetectsSwedishAndPersistsItsSectionHeadings()
    {
        using var factory = new RecipeAppFactory();
        factory.TextExtractor.Text =
            "Äppelkaka\nIngredienser:\n2 ägg\nGör så här:\nBlanda väl.";
        using var client = factory.CreateAppClient();
        var capturePage = await client.GetAsync("/Recipes/Capture");
        var token = ExtractAntiforgeryToken(await capturePage.Content.ReadAsStringAsync());

        using var upload = CreateImageUpload(token, [0xFF, 0xD8, 0xFF, 0xD9]);
        var extractResponse = await client.PostAsync("/Recipes/Capture?handler=Extract", upload);
        var reviewHtml = await extractResponse.Content.ReadAsStringAsync();
        var decodedReviewHtml = WebUtility.HtmlDecode(reviewHtml);

        Assert.Equal(HttpStatusCode.OK, extractResponse.StatusCode);
        Assert.Matches("<option[^>]*value=\"sv\"[^>]*selected=\"selected\"", decodedReviewHtml);
        Assert.Contains(">Ingredienser</label>", decodedReviewHtml, StringComparison.Ordinal);
        Assert.Contains(">Gör så här</label>", decodedReviewHtml, StringComparison.Ordinal);

        var saveResponse = await client.PostAsync(
            "/Recipes/Capture?handler=Save",
            Form(
                ExtractAntiforgeryToken(reviewHtml),
                new Dictionary<string, string>
                {
                    ["Input.Title"] = "Äppelkaka",
                    ["Input.LanguageCode"] = "sv",
                    ["Input.Ingredients"] = "2 ägg",
                    ["Input.Instructions"] = "Blanda väl."
                }));

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var details = WebUtility.HtmlDecode(
            await client.GetStringAsync(saveResponse.Headers.Location!.OriginalString));
        Assert.Matches("<h2[^>]*>\\s*Ingredienser\\s*</h2>", details);
        Assert.Matches("<h2[^>]*>\\s*Gör så här\\s*</h2>", details);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("sv", (await db.Recipes.SingleAsync()).LanguageCode);
    }

    [Fact]
    public async Task CapturePage_RejectsInvalidImagesAndDoesNotSaveUnreadableDrafts()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var capturePage = await client.GetAsync("/Recipes/Capture");
        var token = ExtractAntiforgeryToken(await capturePage.Content.ReadAsStringAsync());

        using (var invalidUpload = CreateImageUpload(token, [0x00, 0x01, 0x02]))
        {
            var invalidResponse = await client.PostAsync("/Recipes/Capture?handler=Extract", invalidUpload);
            Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
            Assert.Contains(
                "Upload a valid JPEG, PNG, or WebP image.",
                VisibleText(await invalidResponse.Content.ReadAsStringAsync()));
            Assert.Equal(0, factory.TextExtractor.CallCount);
        }

        factory.TextExtractor.Text = string.Empty;
        using var emptyUpload = CreateImageUpload(token, [0xFF, 0xD8, 0xFF, 0xD9]);
        var emptyResponse = await client.PostAsync("/Recipes/Capture?handler=Extract", emptyUpload);
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Contains(
            "No readable text was found.",
            VisibleText(await emptyResponse.Content.ReadAsStringAsync()));
        Assert.Equal(1, factory.TextExtractor.CallCount);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Recipes.Where(recipe => recipe.OwnerId == RecipeAppFactory.TestUserId).ToListAsync());
    }

    [Fact]
    public async Task ImportPage_ReviewsAndSavesUserConfirmedRecipe()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var importPage = await client.GetAsync("/Recipes/Import");
        var token = ExtractAntiforgeryToken(await importPage.Content.ReadAsStringAsync());

        var importResponse = await client.PostAsync(
            "/Recipes/Import?handler=Import",
            Form(token, new Dictionary<string, string>
            {
                ["RecipeUrl"] = "https://recipes.example.test/tomato-soup"
            }));

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var reviewHtml = await importResponse.Content.ReadAsStringAsync();
        Assert.Contains("Review the imported recipe", reviewHtml);
        Assert.Contains("value=\"Tomato soup\"", reviewHtml);
        Assert.Contains("4 tomatoes", reviewHtml);
        Assert.Contains("A simple soup.", reviewHtml);
        Assert.Contains("https://recipes.example.test/tomato-soup.jpg", reviewHtml);
        Assert.Equal(1, factory.UrlImporter.CallCount);
        Assert.Equal("https://recipes.example.test/tomato-soup", factory.UrlImporter.LastUri?.AbsoluteUri);

        var reviewToken = ExtractAntiforgeryToken(reviewHtml);
        var saveResponse = await client.PostAsync(
            "/Recipes/Import?handler=Save",
            Form(reviewToken, new Dictionary<string, string>
            {
                ["Input.Title"] = "My corrected tomato soup",
                ["Input.Description"] = "Corrected description.",
                ["Input.ImageUrl"] = "https://recipes.example.test/tomato-soup.jpg",
                ["Input.Ingredients"] = "5 tomatoes\n1 onion",
                ["Input.Instructions"] = "Roast the vegetables.\nBlend until smooth."
            }));

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var details = WebUtility.HtmlDecode(
            await client.GetStringAsync(saveResponse.Headers.Location!.OriginalString));
        Assert.Contains("My corrected tomato soup", details);
        Assert.Contains("5 tomatoes", details);
        Assert.Contains("Corrected description.", details);
    }

    [Fact]
    public async Task ImportPage_AcceptsAddressWithoutScheme()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var importPage = await client.GetAsync("/Recipes/Import");
        var pageHtml = await importPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(pageHtml);

        Assert.DoesNotContain("type=\"url\"", pageHtml);

        var importResponse = await client.PostAsync(
            "/Recipes/Import?handler=Import",
            Form(token, new Dictionary<string, string>
            {
                ["RecipeUrl"] = "recipes.example.test/tomato-soup"
            }));

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.Contains("Review the imported recipe", await importResponse.Content.ReadAsStringAsync());
        Assert.Equal("https://recipes.example.test/tomato-soup", factory.UrlImporter.LastUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ImportPage_UsesRecipeLanguageMetadataForReviewAndSavedHeadings()
    {
        using var factory = new RecipeAppFactory();
        factory.UrlImporter.Draft = new RecipeDraft(
            "Äppelkaka",
            "2 ägg",
            "Blanda väl.",
            LanguageCode: "sv-SE");
        using var client = factory.CreateAppClient();
        var importPage = await client.GetAsync("/Recipes/Import");
        var token = ExtractAntiforgeryToken(await importPage.Content.ReadAsStringAsync());

        var importResponse = await client.PostAsync(
            "/Recipes/Import?handler=Import",
            Form(token, new Dictionary<string, string>
            {
                ["RecipeUrl"] = "https://recipes.example.test/apple-cake"
            }));
        var reviewHtml = await importResponse.Content.ReadAsStringAsync();
        var decodedReviewHtml = WebUtility.HtmlDecode(reviewHtml);

        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.Matches("<option[^>]*value=\"sv\"[^>]*selected=\"selected\"", decodedReviewHtml);
        Assert.Contains(">Ingredienser</label>", decodedReviewHtml, StringComparison.Ordinal);
        Assert.Contains(">Gör så här</label>", decodedReviewHtml, StringComparison.Ordinal);

        var saveResponse = await client.PostAsync(
            "/Recipes/Import?handler=Save",
            Form(
                ExtractAntiforgeryToken(reviewHtml),
                new Dictionary<string, string>
                {
                    ["Input.Title"] = "Äppelkaka",
                    ["Input.LanguageCode"] = "sv",
                    ["Input.Ingredients"] = "2 ägg",
                    ["Input.Instructions"] = "Blanda väl."
                }));

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        var details = WebUtility.HtmlDecode(
            await client.GetStringAsync(saveResponse.Headers.Location!.OriginalString));
        Assert.Matches("<h2[^>]*>\\s*Ingredienser\\s*</h2>", details);
        Assert.Matches("<h2[^>]*>\\s*Gör så här\\s*</h2>", details);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("ftp://recipes.example.test/soup")]
    public async Task ImportPage_RejectsUnsupportedOrPrivateAddresses(string recipeUrl)
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var importPage = await client.GetAsync("/Recipes/Import");
        var token = ExtractAntiforgeryToken(await importPage.Content.ReadAsStringAsync());

        var response = await client.PostAsync(
            "/Recipes/Import?handler=Import",
            Form(token, new Dictionary<string, string> { ["RecipeUrl"] = recipeUrl }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "valid public HTTP or HTTPS recipe page URL",
            VisibleText(await response.Content.ReadAsStringAsync()),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.UrlImporter.CallCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Recipes.Where(recipe => recipe.OwnerId == RecipeAppFactory.TestUserId).ToListAsync());
    }

    [Fact]
    public async Task ImportPage_ShowsClearMessageWhenWebsiteHasNoRecipeData()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        factory.UrlImporter.Failure = new RecipeImportException("No supported recipe data was found.");
        var importPage = await client.GetAsync("/Recipes/Import");
        var token = ExtractAntiforgeryToken(await importPage.Content.ReadAsStringAsync());

        var response = await client.PostAsync(
            "/Recipes/Import?handler=Import",
            Form(token, new Dictionary<string, string>
            {
                ["RecipeUrl"] = "https://recipes.example.test/not-a-recipe"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No supported recipe data was found.", VisibleText(await response.Content.ReadAsStringAsync()));
        Assert.Equal(1, factory.UrlImporter.CallCount);
    }

    [Fact]
    public async Task RecipeDetails_HideRecipesOwnedByAnotherAccount()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        int recipeId;
        int ownRecipeId;

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
            var otherRecipe = new Recipe
            {
                Title = "Private recipe",
                Ingredients = "1 ingredient",
                Instructions = "1 step",
                OwnerId = otherOwnerId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var ownRecipe = new Recipe
            {
                Title = "My recipe",
                Ingredients = "1 ingredient",
                Instructions = "1 step",
                OwnerId = RecipeAppFactory.TestUserId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Recipes.AddRange(otherRecipe, ownRecipe);
            await db.SaveChangesAsync();
            recipeId = otherRecipe.Id;
            ownRecipeId = ownRecipe.Id;
        }

        var recipeList = await client.GetStringAsync("/Recipes");
        Assert.Contains("My recipe", recipeList);
        Assert.DoesNotContain("Private recipe", recipeList);

        foreach (var page in new[] { "Details", "Edit", "Delete" })
        {
            var response = await client.GetAsync($"/Recipes/{page}/{recipeId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var ownDeletePage = await client.GetAsync($"/Recipes/Delete/{ownRecipeId}");
        Assert.Equal(HttpStatusCode.OK, ownDeletePage.StatusCode);
        var token = ExtractAntiforgeryToken(await ownDeletePage.Content.ReadAsStringAsync());
        var deniedDelete = await client.PostAsync(
            $"/Recipes/Delete/{recipeId}",
            Form(token, new Dictionary<string, string> { ["Id"] = recipeId.ToString() }));
        Assert.Equal(HttpStatusCode.NotFound, deniedDelete.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await verificationDb.Recipes.SingleOrDefaultAsync(recipe => recipe.Id == recipeId));
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

    private static MultipartFormDataContent CreateImageUpload(string token, byte[] imageBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        var image = new ByteArrayContent(imageBytes);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "photo", "recipe.jpg");
        return content;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

        Assert.True(match.Success, "The form should contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string VisibleText(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " "));
}
