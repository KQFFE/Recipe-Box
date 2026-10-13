using Microsoft.Playwright;

namespace RecipeManager.BrowserTests;

[CollectionDefinition(Name)]
public sealed class RecipeBrowserJourneysCollection : ICollectionFixture<BrowserTestApplication>
{
    public const string Name = "Recipe browser journeys";
}

[Collection(RecipeBrowserJourneysCollection.Name)]
public sealed class RecipeBrowserJourneysTests(BrowserTestApplication application)
{
    [Fact]
    public async Task UserCanCreateEditAndDeleteRecipe()
    {
        await using var context = await application.CreateContextAsync();
        var page = await context.NewPageAsync();
        await RegisterUserAsync(page);

        await page.GotoAsync($"{application.BaseUrl}/Recipes/Create");
        await page.GetByLabel("Recipe name").FillAsync("Browser-tested tomato soup");
        await page.GetByLabel("Description").FillAsync("Created in a real browser.");
        await page.GetByLabel("Ingredients").FillAsync("4 tomatoes\n1 onion");
        await page.GetByLabel("Instructions").FillAsync("Roast the vegetables.\nBlend until smooth.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "Browser-tested tomato soup");
        await page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await page.GetByLabel("Recipe name").FillAsync("Browser-tested smoky soup");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await ExpectHeadingAsync(page, "Browser-tested smoky soup");
        await page.GetByRole(AriaRole.Link, new() { Name = "Delete" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "My recipes");
        await Assertions.Expect(page.GetByText("Browser-tested smoky soup")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task UserCanReviewAndSavePhotoCaptureDraft()
    {
        await using var context = await application.CreateContextAsync();
        var page = await context.NewPageAsync();
        await RegisterUserAsync(page);
        await page.GotoAsync($"{application.BaseUrl}/Recipes/Capture");

        await page.Locator("#photo").SetInputFilesAsync(new FilePayload
        {
            Name = "recipe.png",
            MimeType = "image/png",
            Buffer = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Extract recipe text" }).ClickAsync();

        await ExpectHeadingAsync(page, "Review the extracted recipe");
        await Assertions.Expect(page.GetByLabel("Recipe name")).ToHaveValueAsync("Browser test soup");
        await Assertions.Expect(page.GetByLabel("Ingredients")).ToContainTextAsync("4 tomatoes");
        await Assertions.Expect(page.GetByLabel("Instructions")).ToContainTextAsync("Roast the vegetables.");
        await page.GetByLabel("Recipe name").FillAsync("My reviewed photo recipe");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "My reviewed photo recipe");
        await Assertions.Expect(page.GetByText("Blend until smooth.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UserCanReviewAndSaveWebsiteImportDraft()
    {
        await using var context = await application.CreateContextAsync();
        var page = await context.NewPageAsync();
        await RegisterUserAsync(page);
        await page.GotoAsync($"{application.BaseUrl}/Recipes/Import");

        await page.GetByLabel("Recipe page URL").FillAsync("https://recipes.example.test/pasta");
        await page.GetByRole(AriaRole.Button, new() { Name = "Find recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "Review the imported recipe");
        await Assertions.Expect(page.GetByLabel("Recipe name")).ToHaveValueAsync("Browser test pasta");
        await Assertions.Expect(page.GetByLabel("Ingredients")).ToContainTextAsync("200 g pasta");
        await Assertions.Expect(page.GetByLabel("Instructions")).ToContainTextAsync("Boil the pasta.");
        await page.GetByLabel("Recipe name").FillAsync("My reviewed pasta");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "My reviewed pasta");
        await Assertions.Expect(page.GetByText("1 cup tomato sauce")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Stir in the sauce.")).ToBeVisibleAsync();
    }

    private async Task RegisterUserAsync(IPage page)
    {
        await page.GotoAsync($"{application.BaseUrl}/Identity/Account/Register");
        await page.GetByLabel("Email").FillAsync($"browser-{Guid.NewGuid():N}@example.test");
        await page.Locator("#Input_Password").FillAsync("RecipeBox-Playwright-2026!");
        await page.Locator("#Input_ConfirmPassword").FillAsync("RecipeBox-Playwright-2026!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Register" }).ClickAsync();
        if (page.Url.EndsWith("/Identity/Account/Register", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Browser-test account registration did not complete: {await page.Locator("body").InnerTextAsync()}\n{application.GetProcessOutput()}");
        }
    }

    private static Task ExpectHeadingAsync(IPage page, string heading) =>
        Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading })).ToBeVisibleAsync();
}
