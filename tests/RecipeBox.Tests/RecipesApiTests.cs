using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeBox.Web.Api;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;

namespace RecipeBox.Tests;

public sealed class RecipesApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RecipesApi_CreatesReadsUpdatesAndDeletesOwnedRecipes()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/recipes", new
        {
            title = "  Tomato soup  ",
            description = " Weeknight soup ",
            languageCode = "en",
            ingredients = new[] { " 4 tomatoes ", "1 onion" },
            instructions = new[] { "Roast vegetables.", "Blend smooth." }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal("/api/v1/recipes/1", createResponse.Headers.Location?.OriginalString);
        var created = await createResponse.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Tomato soup", created.Title);
        Assert.Equal("Weeknight soup", created.Description);
        Assert.Equal(["4 tomatoes", "1 onion"], created.Ingredients);
        Assert.Equal(["Roast vegetables.", "Blend smooth."], created.Instructions);

        var getResponse = await client.GetAsync($"/api/v1/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.Equal(created.Id, getBody?.Id);

        var listResponse = await client.GetAsync("/api/v1/recipes?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<RecipeListResponse>(JsonOptions);
        Assert.NotNull(list);
        Assert.Equal(1, list.TotalCount);
        Assert.Single(list.Items);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/recipes/{created.Id}", new
        {
            title = "Smoky tomato soup",
            description = "Updated.",
            languageCode = "en",
            ingredients = new[] { "4 tomatoes", "1 onion", "1 tsp paprika" },
            instructions = new[] { "Roast vegetables.", "Blend smooth." }
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.Equal("Smoky tomato soup", updated?.Title);
        Assert.Contains("1 tsp paprika", updated?.Ingredients ?? []);

        var deleteResponse = await client.DeleteAsync($"/api/v1/recipes/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/recipes/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task RecipesApi_RoundTripsLayoutAndRejectsInvalidLayouts()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/recipes", new
        {
            title = "Layout soup",
            description = "Warm.",
            languageCode = "en",
            ingredients = new[] { "Tomatoes" },
            instructions = new[] { "Cook." },
            sectionOrder = new[] { "ingredients", "instructions", "description" },
            fullWidthSections = new[] { "description" }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.Equal(["ingredients", "instructions", "description"], created?.SectionOrder);
        Assert.Equal(["description"], created?.FullWidthSections);

        var fetched = await (await client.GetAsync($"/api/v1/recipes/{created!.Id}"))
            .Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.Equal(created.SectionOrder, fetched?.SectionOrder);

        foreach (var invalid in new object[]
        {
            new { sectionOrder = new[] { "ingredients", "bogus" }, fullWidthSections = Array.Empty<string>() },
            new { sectionOrder = new[] { "ingredients", "ingredients" }, fullWidthSections = Array.Empty<string>() },
            new { sectionOrder = new[] { "ingredients" }, fullWidthSections = new[] { "instructions" } },
            new { sectionOrder = new[] { "ingredients" }, fullWidthSections = (string[]?)null },
        })
        {
            var body = JsonSerializer.SerializeToNode(invalid, JsonOptions)!.AsObject();
            body["title"] = "Bad layout";
            body["languageCode"] = "en";
            body["ingredients"] = JsonSerializer.SerializeToNode(new[] { "Tomatoes" });
            body["instructions"] = JsonSerializer.SerializeToNode(new[] { "Cook." });
            var response = await client.PutAsJsonAsync($"/api/v1/recipes/{created.Id}", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task RecipesApi_CreatesReadsAndRevokesShareLinksForTheOwnerOnly()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var created = await (await client.PostAsJsonAsync("/api/v1/recipes", new
        {
            title = "Shared soup",
            languageCode = "en",
            ingredients = new[] { "Tomatoes" },
            instructions = new[] { "Cook." }
        })).Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);

        var inactive = await (await client.GetAsync($"/api/v1/recipes/{created!.Id}/share"))
            .Content.ReadFromJsonAsync<RecipeShareResponse>(JsonOptions);
        Assert.False(inactive?.Active);

        var createShare = await client.PostAsync($"/api/v1/recipes/{created.Id}/share", null);
        Assert.Equal(HttpStatusCode.OK, createShare.StatusCode);
        var share = await createShare.Content.ReadFromJsonAsync<RecipeShareResponse>(JsonOptions);
        Assert.True(share?.Active);
        Assert.Contains("/Recipes/Shared/", share?.Url);

        var sharedPage = await client.GetAsync(new Uri(share!.Url!).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, sharedPage.StatusCode);

        var active = await (await client.GetAsync($"/api/v1/recipes/{created.Id}/share"))
            .Content.ReadFromJsonAsync<RecipeShareResponse>(JsonOptions);
        Assert.True(active?.Active);
        Assert.Null(active?.Url);

        using var otherUser = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/recipes/{created.Id}/share");
        otherUser.Headers.Add("X-Test-UserId", "someone-else");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(otherUser)).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/v1/recipes/{created.Id}/share")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri(share.Url!).PathAndQuery)).StatusCode);
    }

    [Fact]
    public async Task RecipesApi_RequiresAuthenticationAndScopes()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        using var unauthenticated = new HttpRequestMessage(HttpMethod.Get, "/api/v1/recipes");
        unauthenticated.Headers.Add("X-Test-Unauthenticated", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(unauthenticated)).StatusCode);

        using var readOnly = new HttpRequestMessage(HttpMethod.Post, "/api/v1/recipes")
        {
            Content = JsonContent.Create(new
            {
                title = "Soup",
                ingredients = new[] { "Tomatoes" },
                instructions = new[] { "Cook." }
            })
        };
        readOnly.Headers.Add("X-Test-Scopes", "recipes.read");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(readOnly)).StatusCode);
    }

    [Fact]
    public async Task RecipesApi_ValidatesRequestsAndHidesOtherOwnersRecipes()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var invalidResponse = await client.PostAsJsonAsync("/api/v1/recipes", new
        {
            title = "",
            languageCode = "en",
            ingredients = Array.Empty<string>(),
            instructions = new[] { "Cook." }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidResponse.Content.Headers.ContentType?.MediaType);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser
        {
            Id = "other-recipe-owner",
            UserName = "other@example.test",
            NormalizedUserName = "OTHER@EXAMPLE.TEST",
            Email = "other@example.test",
            NormalizedEmail = "OTHER@EXAMPLE.TEST",
            EmailConfirmed = true
        });
        var otherRecipe = new Recipe
        {
            Title = "Private recipe",
            Ingredients = "1 ingredient",
            Instructions = "1 step",
            OwnerId = "other-recipe-owner",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Recipes.Add(otherRecipe);
        await db.SaveChangesAsync();

        using var getOtherRecipe = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/recipes/{otherRecipe.Id}");
        getOtherRecipe.Headers.Add("X-Test-UserId", RecipeAppFactory.TestUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(getOtherRecipe)).StatusCode);

        using var updateOtherRecipe = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/recipes/{otherRecipe.Id}")
        {
            Content = JsonContent.Create(new
            {
                title = "Tampered recipe",
                languageCode = "en",
                ingredients = new[] { "Tampered ingredient" },
                instructions = new[] { "Tampered step" }
            })
        };
        updateOtherRecipe.Headers.Add("X-Test-UserId", RecipeAppFactory.TestUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(updateOtherRecipe)).StatusCode);

        using var deleteOtherRecipe = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/recipes/{otherRecipe.Id}");
        deleteOtherRecipe.Headers.Add("X-Test-UserId", RecipeAppFactory.TestUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(deleteOtherRecipe)).StatusCode);

        using var listOtherRecipes = new HttpRequestMessage(HttpMethod.Get, "/api/v1/recipes");
        listOtherRecipes.Headers.Add("X-Test-UserId", RecipeAppFactory.TestUserId);
        var listResponse = await client.SendAsync(listOtherRecipes);
        var list = await listResponse.Content.ReadFromJsonAsync<RecipeListResponse>(JsonOptions);
        Assert.Empty(list!.Items);
        Assert.Equal("Private recipe", (await db.Recipes.FindAsync(otherRecipe.Id))?.Title);
    }

    [Fact]
    public async Task RecipesApi_RejectsInvalidPagination()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var response = await client.GetAsync("/api/v1/recipes?page=0&pageSize=101");
        var overflowResponse = await client.GetAsync($"/api/v1/recipes?page={int.MaxValue}&pageSize=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overflowResponse.StatusCode);
    }

    [Fact]
    public async Task RecipesApi_RejectsRequestBodiesLargerThan64KiB()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var content = new StringContent(
            $"{{\"title\":\"Soup\",\"description\":\"{new string('x', 65_536)}\"}}",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/v1/recipes", content);

        Assert.Equal((HttpStatusCode)413, response.StatusCode);
    }

    [Fact]
    public async Task RecipesApi_StoresAndServesImagesOnlyToTheOwner()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        var createResponse = await client.PostAsJsonAsync("/api/v1/recipes", new
        {
            title = "Photo soup",
            languageCode = "en",
            ingredients = new[] { "Tomatoes" },
            instructions = new[] { "Cook." }
        });
        var recipe = await createResponse.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions);
        Assert.NotNull(recipe);
        var updatedAtBeforeUpload = recipe.UpdatedAtUtc;
        await Task.Delay(10);

        using var imageContent = new MultipartFormDataContent();
        imageContent.Add(
            new ByteArrayContent([0xFF, 0xD8, 0xFF, 0x00]),
            "image",
            "cover.jpg");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/recipes/{recipe.Id}/image")
        {
            Content = imageContent
        };
        var uploadResponse = await client.SendAsync(uploadRequest);
        Assert.Equal(HttpStatusCode.NoContent, uploadResponse.StatusCode);
        Assert.Equal(1, factory.ImageStorage.Count);
        var recipeAfterUpload = await client.GetFromJsonAsync<RecipeResponse>(
            $"/api/v1/recipes/{recipe.Id}",
            JsonOptions);
        Assert.NotNull(recipeAfterUpload);
        Assert.True(recipeAfterUpload.UpdatedAtUtc > updatedAtBeforeUpload);

        var imageResponse = await client.GetAsync($"/api/v1/recipes/{recipe.Id}/image");
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/jpeg", imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", imageResponse.Headers.CacheControl?.ToString());
        Assert.Equal(
            new byte[] { 0xFF, 0xD8, 0xFF, 0x00 },
            await imageResponse.Content.ReadAsByteArrayAsync());

        using var otherOwnerGet = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/recipes/{recipe.Id}/image");
        otherOwnerGet.Headers.Add("X-Test-UserId", "another-recipe-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(otherOwnerGet)).StatusCode);

        using var otherOwnerDelete = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/recipes/{recipe.Id}/image");
        otherOwnerDelete.Headers.Add("X-Test-UserId", "another-recipe-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(otherOwnerDelete)).StatusCode);
        Assert.Equal(1, factory.ImageStorage.Count);
        using var otherOwnerUploadContent = new MultipartFormDataContent();
        otherOwnerUploadContent.Add(
            new ByteArrayContent([0xFF, 0xD8, 0xFF, 0x01]),
            "image",
            "other.jpg");
        using var otherOwnerUpload = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/recipes/{recipe.Id}/image")
        {
            Content = otherOwnerUploadContent
        };
        otherOwnerUpload.Headers.Add("X-Test-UserId", "another-recipe-owner");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(otherOwnerUpload)).StatusCode);
        Assert.Equal(1, factory.ImageStorage.Count);

        await Task.Delay(10);
        var deleteResponse = await client.DeleteAsync($"/api/v1/recipes/{recipe.Id}/image");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(0, factory.ImageStorage.Count);
        var recipeAfterDelete = await client.GetFromJsonAsync<RecipeResponse>(
            $"/api/v1/recipes/{recipe.Id}",
            JsonOptions);
        Assert.NotNull(recipeAfterDelete);
        Assert.True(recipeAfterDelete.UpdatedAtUtc > recipeAfterUpload.UpdatedAtUtc);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/recipes/{recipe.Id}/image")).StatusCode);
    }

    [Fact]
    public async Task RecipesApi_ReturnsReviewedPhotoDraftWithoutSavingThePhoto()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0xFF, 0xD8, 0xFF, 0x00]), "photo", "capture.jpg");

        var response = await client.PostAsync("/api/v1/recipes/drafts/photo", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<RecipeDraftResponse>(JsonOptions);
        Assert.NotNull(draft);
        Assert.Equal("Tomato soup", draft.Title);
        Assert.Equal(["4 tomatoes", "1 onion"], draft.Ingredients);
        Assert.Equal(["Roast the vegetables.", "Blend until smooth."], draft.Instructions);
        Assert.Equal(1, factory.TextExtractor.CallCount);
        Assert.Equal(0, factory.ImageStorage.Count);

        using var unsupportedForm = new MultipartFormDataContent();
        unsupportedForm.Add(new ByteArrayContent([0x00, 0x01, 0x02]), "photo", "capture.bin");
        var unsupportedResponse = await client.PostAsync("/api/v1/recipes/drafts/photo", unsupportedForm);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
        Assert.Equal(1, factory.TextExtractor.CallCount);
    }

    [Fact]
    public async Task RecipesApi_ReturnsEditableImportDraftAndRejectsPrivateUrls()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/recipes/drafts/import",
            new { url = "https://recipes.example.test/tomato-soup" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<RecipeDraftResponse>(JsonOptions);
        Assert.NotNull(draft);
        Assert.Equal("Tomato soup", draft.Title);
        Assert.Equal("en", draft.LanguageCode);
        Assert.Equal(["4 tomatoes", "1 onion"], draft.Ingredients);
        Assert.Equal("https://recipes.example.test/tomato-soup", factory.UrlImporter.LastUri?.AbsoluteUri);

        var privateUrlResponse = await client.PostAsJsonAsync(
            "/api/v1/recipes/drafts/import",
            new { url = "http://127.0.0.1/recipe" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, privateUrlResponse.StatusCode);
        Assert.Equal(1, factory.UrlImporter.CallCount);
    }

    [Fact]
    public async Task OpenIddict_AdvertisesCodeFlowAndUsesIdentityLogin()
    {
        using var factory = new RecipeAppFactory();
        factory.UseTestApiAuthorization = false;
        using var client = factory.CreateAppClient();

        var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        var discoveryContent = await discoveryResponse.Content.ReadAsStringAsync();
        Assert.True(
            discoveryResponse.StatusCode == HttpStatusCode.OK,
            $"Discovery returned {(int)discoveryResponse.StatusCode}: {discoveryContent}");
        using var discovery = JsonDocument.Parse(discoveryContent);
        Assert.Contains(
            "authorization_code",
            discovery.RootElement.GetProperty("grant_types_supported")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains(
            "refresh_token",
            discovery.RootElement.GetProperty("grant_types_supported")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.EndsWith(
            "/connect/authorize",
            discovery.RootElement.GetProperty("authorization_endpoint").GetString(),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "/connect/token",
            discovery.RootElement.GetProperty("token_endpoint").GetString(),
            StringComparison.Ordinal);

        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var codeChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizationResponse = await client.GetAsync(
            "/connect/authorize?client_id=recipebox-mobile"
            + "&redirect_uri=com.kqffe.recipebox%3A%2F%2Fcallback"
            + "&response_type=code"
            + "&scope=openid%20profile%20offline_access%20recipes.read%20recipes.write"
            + $"&code_challenge={codeChallenge}"
            + "&code_challenge_method=S256"
            + "&state=mobile-test-state");
        Assert.Equal(HttpStatusCode.Redirect, authorizationResponse.StatusCode);
        var callback = authorizationResponse.Headers.Location!;
        Assert.Equal("com.kqffe.recipebox", callback.Scheme);
        var callbackParameters = QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal("mobile-test-state", callbackParameters["state"].ToString());
        var authorizationCode = callbackParameters["code"].ToString();
        Assert.NotEmpty(authorizationCode);

        var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "recipebox-mobile",
                ["redirect_uri"] = "com.kqffe.recipebox://callback",
                ["code"] = authorizationCode,
                ["code_verifier"] = codeVerifier
            }));
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        Assert.True(tokens.RootElement.TryGetProperty("access_token", out var accessToken));
        var bearerToken = accessToken.GetString();
        Assert.False(string.IsNullOrWhiteSpace(bearerToken));
        Assert.True(tokens.RootElement.TryGetProperty("refresh_token", out var refreshToken));
        var currentRefreshToken = refreshToken.GetString();
        Assert.False(string.IsNullOrWhiteSpace(currentRefreshToken));

        var refreshResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "recipebox-mobile",
                ["refresh_token"] = currentRefreshToken!
            }));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        using var refreshedTokens = JsonDocument.Parse(await refreshResponse.Content.ReadAsStringAsync());
        bearerToken = refreshedTokens.RootElement.GetProperty("access_token").GetString();
        currentRefreshToken = refreshedTokens.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(bearerToken));
        Assert.False(string.IsNullOrWhiteSpace(currentRefreshToken));
        Assert.NotEqual(refreshToken.GetString(), currentRefreshToken);

        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/recipes");
        apiRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        var apiResponse = await client.SendAsync(apiRequest);
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);

        var revokeResponse = await client.PostAsync(
            "/connect/revoke",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "recipebox-mobile",
                ["token"] = currentRefreshToken!,
                ["token_type_hint"] = "refresh_token"
            }));
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var revokedRefreshResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "recipebox-mobile",
                ["refresh_token"] = currentRefreshToken!
            }));
        Assert.Equal(HttpStatusCode.BadRequest, revokedRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task OpenIddict_RedirectsUnauthenticatedAuthorizationToIdentityLogin()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/connect/authorize?client_id=recipebox-mobile"
            + "&redirect_uri=com.kqffe.recipebox%3A%2F%2Fcallback"
            + "&response_type=code"
            + "&scope=openid%20recipes.read"
            + "&code_challenge=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"
            + "&code_challenge_method=S256");
        request.Headers.Add("X-Test-Unauthenticated", "true");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Contains("ReturnUrl", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task OpenIddict_RejectsAuthorizationRequestsWithoutPkce()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var response = await client.GetAsync(
            "/connect/authorize?client_id=recipebox-mobile"
            + "&redirect_uri=com.kqffe.recipebox%3A%2F%2Fcallback"
            + "&response_type=code"
            + "&scope=openid%20recipes.read"
            + "&state=mobile-test-state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "invalid_request",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}
