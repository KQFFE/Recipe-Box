using RecipeManager.Web.Models;

namespace RecipeManager.Tests;

public sealed class RecipeLayoutTests
{
    [Fact]
    public void DefaultLayoutUsesFullWidthForDescriptionAndImage()
    {
        var recipe = new Recipe
        {
            Description = "Description",
            ImageUrl = "https://example.test/image.jpg"
        };

        var layout = RecipeLayoutDefinition.Default(recipe);

        Assert.Equal(
            new[] { "description", "image", "ingredients", "instructions" },
            layout.Sections.Select(section => section.Key));
        Assert.Equal(new[] { "description", "image" }, layout.FullWidthSections.Order());
    }

    [Fact]
    public void TryCreateAcceptsAnOrderAndWidthSelectionForAvailableSections()
    {
        var available = RecipeLayoutDefinition.AvailableSections(new Recipe
        {
            Description = "Description",
            ImageUrl = "https://example.test/image.jpg"
        });

        var isValid = RecipeLayoutDefinition.TryCreate(
            available,
            ["instructions", "ingredients", "image", "description"],
            ["instructions", "image"],
            out var layout);

        Assert.True(isValid);
        Assert.Equal(
            new[] { "instructions", "ingredients", "image", "description" },
            layout.Sections.Select(section => section.Key));
        Assert.Equal(new[] { "image", "instructions" }, layout.FullWidthSections.Order());
    }

    [Theory]
    [InlineData(new[] { "ingredients", "ingredients" }, new[] { "ingredients" })]
    [InlineData(new[] { "ingredients", "unknown" }, new[] { "ingredients" })]
    [InlineData(new[] { "ingredients" }, new[] { "ingredients" })]
    [InlineData(new[] { "ingredients", "instructions" }, new[] { "unknown" })]
    [InlineData(new[] { "ingredients", "instructions" }, new[] { "ingredients", "ingredients" })]
    public void TryCreateRejectsIncompleteDuplicateOrUnsupportedSections(
        string[] sectionOrder,
        string[] fullWidthSections)
    {
        var available = RecipeLayoutDefinition.AvailableSections(new Recipe());

        Assert.False(RecipeLayoutDefinition.TryCreate(
            available,
            sectionOrder,
            fullWidthSections,
            out _));
    }
}
