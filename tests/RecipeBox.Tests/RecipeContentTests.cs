using System.ComponentModel.DataAnnotations;
using RecipeBox.Web.Pages.Recipes;
using RecipeBox.Web.Models;

namespace RecipeBox.Tests;

public sealed class RecipeContentTests
{
    [Fact]
    public void NormalizeLines_TrimsEntriesAndRemovesBlankLines()
    {
        var content = "  2 eggs \r\n\r\n 1 cup flour  \n";

        var result = RecipeContent.NormalizeLines(content);

        Assert.Equal($"2 eggs{Environment.NewLine}1 cup flour", result);
    }

    [Fact]
    public void ToLines_ReturnsSeparateOrderedItems()
    {
        var result = RecipeContent.ToLines("Mix well\r\nBake for 20 minutes");

        Assert.Equal(new[] { "Mix well", "Bake for 20 minutes" }, result);
    }

    [Fact]
    public void ToLines_ReturnsEmptyListForBlankContent()
    {
        Assert.Empty(RecipeContent.ToLines(" \r\n "));
    }

    [Fact]
    public void RecipeDraftParser_SeparatesRecipeSections()
    {
        var draft = RecipeDraftParser.Parse(
            "Tomato soup\nIngredients:\n4 tomatoes\n1 onion\nDirections:\nRoast vegetables.\nBlend until smooth.");

        Assert.Equal("Tomato soup", draft.Title);
        Assert.Equal($"4 tomatoes{Environment.NewLine}1 onion", draft.Ingredients);
        Assert.Equal($"Roast vegetables.{Environment.NewLine}Blend until smooth.", draft.Instructions);
    }

    [Fact]
    public void RecipeDraftParser_RecognizesCaseInsensitiveMethodHeading()
    {
        var draft = RecipeDraftParser.Parse("Toast\n1 slice bread\nMETHOD:\nToast the bread.");

        Assert.Equal("1 slice bread", draft.Ingredients);
        Assert.Equal("Toast the bread.", draft.Instructions);
    }

    [Fact]
    public void RecipeDraftParser_RecognizesSwedishHeadingsAndCleansOcrListMarkers()
    {
        var draft = RecipeDraftParser.Parse(
            "Äppelkaka\nIngredienser: © Skriv ut\n+ 3 st ägg\n© Laktosfritt\nGör så här:\n• Blanda ingredienserna.");

        Assert.Equal("Äppelkaka", draft.Title);
        Assert.Equal($"3 st ägg{Environment.NewLine}Laktosfritt", draft.Ingredients);
        Assert.Equal("Blanda ingredienserna.", draft.Instructions);
    }

    [Fact]
    public void RecipeFormModel_RejectsContentWithoutIngredientsOrInstructions()
    {
        var model = new RecipeFormModel
        {
            Title = "Tomato soup",
            Ingredients = " \r\n ",
            Instructions = "\n"
        };

        var results = Validate(model);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.Ingredients)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.Instructions)));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private/image.png")]
    [InlineData("/images/soup.png")]
    [InlineData("http:example.test/soup.png")]
    public void RecipeFormModel_RejectsNonHttpImageUrls(string imageUrl)
    {
        var model = new RecipeFormModel
        {
            Title = "Tomato soup",
            Ingredients = "Tomatoes",
            Instructions = "Roast the tomatoes.",
            ImageUrl = imageUrl
        };

        var results = Validate(model);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.ImageUrl)));
    }

    [Theory]
    [InlineData("https://example.test/soup.png")]
    [InlineData("http://example.test/soup.png")]
    public void RecipeFormModel_AcceptsHttpImageUrls(string imageUrl)
    {
        var model = new RecipeFormModel
        {
            Title = "Tomato soup",
            Ingredients = "Tomatoes",
            Instructions = "Roast the tomatoes.",
            ImageUrl = imageUrl
        };

        Assert.Empty(Validate(model));
    }

    private static IList<ValidationResult> Validate(RecipeFormModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
