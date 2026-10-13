using RecipeBox.Web.Models;

namespace RecipeBox.Web.Services;

public sealed class BrowserTestRecipeTextExtractor : IRecipeTextExtractor
{
    public string ExtractText(byte[] imageBytes) =>
        "Browser test soup\nIngredients:\n4 tomatoes\n1 onion\nInstructions:\nRoast the vegetables.\nBlend until smooth.";
}

public sealed class BrowserTestRecipeUrlImporter : IRecipeUrlImporter
{
    public Task<RecipeDraft> ImportAsync(Uri recipeUri, CancellationToken cancellationToken) =>
        Task.FromResult(new RecipeDraft(
            "Browser test pasta",
            "200 g pasta\n1 cup tomato sauce",
            "Boil the pasta.\nStir in the sauce.",
            "A quick browser-test recipe.",
            "https://recipes.example.test/browser-test-pasta.jpg"));
}
