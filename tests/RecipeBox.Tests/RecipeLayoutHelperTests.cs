using RecipeBox.Mobile.Core;

namespace RecipeBox.Tests;

public sealed class RecipeLayoutHelperTests
{
    private static StoredRecipe Recipe(bool description = true, bool image = false) => new()
    {
        Title = "Soup",
        Description = description ? "Warm" : null,
        CoverImagePath = image ? "cover.jpg" : null,
        Ingredients = ["Tomatoes"],
        Instructions = ["Cook"],
    };

    [Fact]
    public void Resolve_UsesWebDefaultWhenNothingIsStored()
    {
        var layout = RecipeLayout.Resolve(Recipe(image: true));

        Assert.Equal(["description", "image", "ingredients", "instructions"], layout.Order);
        Assert.Equal(["description", "image"], layout.FullWidth.Order());
    }

    [Fact]
    public void Resolve_SkipsUnavailableSectionsAndAppendsNewOnes()
    {
        var recipe = Recipe(description: false, image: true);
        recipe.LayoutOrder = "instructions|description|ingredients";
        recipe.LayoutFullWidth = "description|instructions";

        var layout = RecipeLayout.Resolve(recipe);

        Assert.Equal(["instructions", "ingredients", "image"], layout.Order);
        Assert.Equal(["image", "instructions"], layout.FullWidth.Order());
    }

    [Fact]
    public void Save_StoresOrderAndFullWidthInOrder()
    {
        var recipe = Recipe();
        RecipeLayout.Save(
            recipe,
            ["ingredients", "description", "instructions"],
            new HashSet<string> { "instructions", "description" });

        Assert.Equal("ingredients|description|instructions", recipe.LayoutOrder);
        Assert.Equal("description|instructions", recipe.LayoutFullWidth);
    }
}
