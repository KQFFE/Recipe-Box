using Microsoft.Playwright;

namespace RecipeBox.BrowserTests;

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
        await page.GetByLabel("Upload a cover image").SetInputFilesAsync(new FilePayload
        {
            Name = "tomato-soup.png",
            MimeType = "image/png",
            Buffer = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        });
        await page.GetByLabel("Ingredients").FillAsync("4 tomatoes\n1 onion");
        await page.GetByLabel("Instructions").FillAsync("Roast the vegetables.\nBlend until smooth.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save recipe" }).ClickAsync();

        await ExpectHeadingAsync(page, "Browser-tested tomato soup");
        Assert.Contains("Recipe Box", await page.TitleAsync(), StringComparison.Ordinal);
        await Assertions.Expect(page.Locator("footer")).ToContainTextAsync("Recipe Box");
        await Assertions.Expect(page.Locator("footer")).Not.ToContainTextAsync("RecipeBox.Web");
        var privateImagePath = await page.Locator("#recipe-section-image img").GetAttributeAsync("src");
        var privateImageContentType = await page.EvaluateAsync<string>(
            "async path => (await (await fetch(path)).blob()).type",
            privateImagePath);
        Assert.Equal("image/png", privateImageContentType);
        await page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await page.GetByLabel("Recipe name").FillAsync("Browser-tested smoky soup");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await ExpectHeadingAsync(page, "Browser-tested smoky soup");
        await page.GetByRole(AriaRole.Link, new() { Name = "Customize layout" }).ClickAsync();
        var layoutPreview = page.Locator(".layout-editor-preview");
        await Assertions.Expect(layoutPreview.GetByText("Created in a real browser.").First).ToBeVisibleAsync();
        var actionSpacing = await page.Locator(".layout-editor-actions")
            .EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).marginTop)");
        Assert.True(actionSpacing >= 32, "Layout action buttons should be separated from the preview.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Move How to prepare up" }).ClickAsync();
        var accessibleMoveOrder = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => section.getAttribute('data-layout-section'))");
        Assert.Equal(new[] { "description", "image", "instructions", "ingredients" }, accessibleMoveOrder);
        var imageBounds = await layoutPreview.Locator("#recipe-section-image").BoundingBoxAsync();
        Assert.NotNull(imageBounds);
        await layoutPreview.Locator("#recipe-section-description")
            .DragToAsync(
                layoutPreview.Locator("#recipe-section-image"),
                new() { TargetPosition = new TargetPosition { X = 10, Y = imageBounds.Height / 2 } });
        var previewOrder = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => section.getAttribute('data-layout-section'))");
        Assert.Equal(new[] { "image", "description", "instructions", "ingredients" }, previewOrder);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Move Description up" }))
            .ToBeEnabledAsync();
        await layoutPreview.GetByLabel("Full width for How to prepare").CheckAsync();
        var previewInstructionClasses = await layoutPreview
            .Locator("#recipe-section-instructions")
            .GetAttributeAsync("class");
        Assert.Contains("recipe-layout-panel-full", previewInstructionClasses);

        await page.SetViewportSizeAsync(1920, 1080);
        var desktopSectionsVisible = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<bool[]>("sections => sections.map(section => section.getBoundingClientRect().bottom <= innerHeight)");
        Assert.All(desktopSectionsVisible, Assert.True);
        var desktopCardHeights = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<double[]>("sections => sections.map(section => section.getBoundingClientRect().height)");
        Assert.All(desktopCardHeights, height => Assert.Equal(desktopCardHeights[0], height, 1));
        await page.SetViewportSizeAsync(375, 667);
        var mobileSectionBounds = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => `${section.dataset.layoutSection}: ${Math.round(section.getBoundingClientRect().top)}-${Math.round(section.getBoundingClientRect().bottom)}`)");
        var mobileSectionsFit = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<bool[]>("sections => sections.map(section => section.getBoundingClientRect().bottom <= innerHeight)");
        var mobileCardParts = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => `${section.dataset.layoutSection} column=${getComputedStyle(section).gridColumn} children: ${[...section.children].map(child => `${child.className || child.tagName}=${Math.round(child.getBoundingClientRect().height)}`).join(', ')}`)");
        Assert.True(
            mobileSectionsFit.All(fits => fits),
            $"Sections should fit in the iPhone 8 viewport: {string.Join(", ", mobileSectionBounds)}. {string.Join("; ", mobileCardParts)}");
        var mobileCardHeights = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<double[]>("sections => sections.map(section => section.getBoundingClientRect().height)");
        Assert.All(mobileCardHeights, height => Assert.Equal(mobileCardHeights[0], height, 1));
        var dragAndCheckboxShareRow = await layoutPreview.Locator("#recipe-section-image")
            .EvaluateAsync<bool>(
                """
                section => {
                    const drag = section.querySelector("[data-layout-drag-handle]").getBoundingClientRect();
                    const checkbox = section.querySelector("[data-layout-width]").getBoundingClientRect();
                    const dragCenterY = drag.top + drag.height / 2;
                    const checkboxCenterY = checkbox.top + checkbox.height / 2;
                    return Math.abs(dragCenterY - checkboxCenterY) < 2 && checkbox.left - drag.right >= 4;
                }
                """);
        Assert.True(dragAndCheckboxShareRow, "The drag handle and width checkbox should have a visible gap on the same row.");
        await Assertions.Expect(layoutPreview.GetByRole(AriaRole.Button, new() { Name = "View full Ingredients" }))
            .ToBeVisibleAsync();
        await layoutPreview.GetByRole(AriaRole.Button, new() { Name = "View full Ingredients" }).ClickAsync();
        await Assertions.Expect(layoutPreview.GetByText("1 onion").Last).ToBeVisibleAsync();
        await Assertions.Expect(layoutPreview.GetByRole(AriaRole.Button, new() { Name = "Hide full Ingredients" }))
            .ToBeVisibleAsync();
        await layoutPreview.GetByRole(AriaRole.Button, new() { Name = "Hide full Ingredients" }).ClickAsync();

        await page.SetViewportSizeAsync(320, 700);
        var editorBounds = await page.Locator(".layout-editor-page").BoundingBoxAsync();
        Assert.NotNull(editorBounds);
        Assert.Equal(0, editorBounds.X);
        Assert.Equal(320, editorBounds.Width);
        var mobileDimensions = await page.EvaluateAsync<int[]>(
            "() => [document.documentElement.clientWidth, document.documentElement.scrollWidth]");
        Assert.Equal(mobileDimensions[0], mobileDimensions[1]);
        await page.EvaluateAsync(
            """
            () => {
                window.scrollTo(0, 0);
                window.layoutRequestedScroll = 0;
                window.originalLayoutScrollBy = window.scrollBy.bind(window);
                window.scrollBy = (x, y) => {
                    window.layoutRequestedScroll += Math.abs(y);
                    window.originalLayoutScrollBy(x, y);
                };
                const handle = document.querySelector("[data-layout-drag-handle='ingredients']");
                const rect = handle.getBoundingClientRect();
                const pointer = (type, x, y) => new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 8, pointerType: "touch", clientX: x, clientY: y
                });
                handle.dispatchEvent(pointer("pointerdown", rect.left + 10, rect.top + 10));
                window.dispatchEvent(pointer("pointermove", 100, window.innerHeight - 2));
            }
            """);
        await page.WaitForTimeoutAsync(250);
        var scrollDistanceRequested = await page.EvaluateAsync<int>("() => window.layoutRequestedScroll");
        Assert.True(
            scrollDistanceRequested >= 2000,
            $"Holding a section at the viewport edge should scroll quickly; requested {scrollDistanceRequested}px in 250ms.");
        await page.EvaluateAsync(
            """
            () => {
                window.dispatchEvent(new PointerEvent("pointercancel", {
                    bubbles: true, cancelable: true, pointerId: 8, pointerType: "touch"
                }));
                window.scrollBy = window.originalLayoutScrollBy;
                delete window.originalLayoutScrollBy;
                window.scrollTo(0, 0);
            }
            """);
        await page.EvaluateAsync(
            """
            () => {
                const handle = document.querySelector("[data-layout-drag-handle='ingredients']");
                const target = document.querySelector("#recipe-section-image");
                const rect = target.getBoundingClientRect();
                const pointer = (type, x, y) => new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 7, pointerType: "touch", clientX: x, clientY: y
                });
                handle.dispatchEvent(pointer("pointerdown", rect.left + 20, rect.top + 20));
                window.dispatchEvent(pointer("pointermove", rect.left + 20, rect.top + 20));
            }
            """);
        await Assertions.Expect(
            layoutPreview.Locator("#recipe-section-image").GetByText("Put here"))
            .ToBeVisibleAsync();
        await page.EvaluateAsync(
            """
            () => {
                const rect = document.querySelector("#recipe-section-image").getBoundingClientRect();
                const pointer = (type, x, y) => new PointerEvent(type, {
                    bubbles: true, cancelable: true, pointerId: 7, pointerType: "touch", clientX: x, clientY: y
                });
                window.dispatchEvent(pointer("pointerup", rect.left + 20, rect.top + 20));
            }
            """);
        var mobileOrder = await layoutPreview.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => section.getAttribute('data-layout-section'))");
        Assert.Equal(new[] { "ingredients", "description", "instructions", "image" }, mobileOrder);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save layout" }).ClickAsync();

        var sectionOrder = await page.Locator("[data-layout-section]")
            .EvaluateAllAsync<string[]>("sections => sections.map(section => section.getAttribute('data-layout-section'))");
        Assert.Equal(new[] { "ingredients", "description", "instructions", "image" }, sectionOrder);
        var instructionClasses = await page.Locator("#recipe-section-instructions").GetAttributeAsync("class");
        Assert.Contains("recipe-layout-panel-full", instructionClasses);

        await page.GetByRole(AriaRole.Link, new() { Name = "Share" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create share link" }).ClickAsync();
        var shareUrl = await page.Locator("#share-link").InputValueAsync();
        await using (var anonymousContext = await application.CreateContextAsync())
        {
            var sharedPage = await anonymousContext.NewPageAsync();
            await sharedPage.GotoAsync(shareUrl);
            await ExpectHeadingAsync(sharedPage, "Browser-tested smoky soup");
            await Assertions.Expect(sharedPage.GetByText("Created in a real browser.")).ToBeVisibleAsync();
            await Assertions.Expect(sharedPage.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);
            var sharedImagePath = await sharedPage.Locator("#recipe-section-image img").GetAttributeAsync("src");
            var sharedImageStatus = await sharedPage.EvaluateAsync<int>(
                "async path => (await fetch(path)).status",
                sharedImagePath);
            Assert.Equal(200, sharedImageStatus);
            var sharedSectionOrder = await sharedPage.Locator("[data-layout-section]")
                .EvaluateAllAsync<string[]>("sections => sections.map(section => section.getAttribute('data-layout-section'))");
            Assert.Equal(new[] { "ingredients", "description", "instructions", "image" }, sharedSectionOrder);

            await page.GetByRole(AriaRole.Button, new() { Name = "Revoke current link" }).ClickAsync();
            var revokedResponse = await sharedPage.GotoAsync(shareUrl);
            Assert.Equal(404, revokedResponse!.Status);
        }

        await page.GetByRole(AriaRole.Link, new() { Name = "Back to recipe" }).ClickAsync();
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
