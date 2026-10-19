using System.Net;
using System.Text;
using RecipeBox.Mobile.Core;

namespace RecipeBox.Tests;

public sealed class RecipePageImporterTests
{
    private const string StructuredPage = """
        <html><head><script type="application/ld+json">
        {"@context":"https://schema.org","@graph":[{"@type":"Recipe","name":"Cheesecake",
        "description":"Creamy.","image":["/img/cake.jpg"],
        "recipeIngredient":["1 cup sugar","2 eggs"],
        "recipeInstructions":[{"@type":"HowToStep","text":"Mix."},{"@type":"HowToStep","text":"Bake."}]}]}
        </script></head><body></body></html>
        """;

    private const string TextPage = """
        <html><head><title>Plain soup</title></head><body><h1>Plain soup</h1>
        <h2>Ingredients</h2><ul><li>4 tomatoes</li><li>1 onion</li></ul>
        <h2>Instructions</h2><ol><li>Chop.</li><li>Simmer.</li></ol></body></html>
        """;

    [Fact]
    public async Task ImportAsync_ReadsSchemaOrgRecipeAndAddsSchemeToBareAddresses()
    {
        var handler = new FakeHandler(StructuredPage);
        var importer = new RecipePageImporter(new HttpClient(handler));

        var recipe = await importer.ImportAsync("sugarspunrun.com/best-cheesecake-recipe/");

        Assert.Equal("https://sugarspunrun.com/best-cheesecake-recipe/", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Cheesecake", recipe.Title);
        Assert.Equal("Creamy.", recipe.Description);
        Assert.Equal(["1 cup sugar", "2 eggs"], recipe.IngredientLines);
        Assert.Equal(["Mix.", "Bake."], recipe.InstructionLines);
        Assert.Equal("https://sugarspunrun.com/img/cake.jpg", recipe.ImageUrl);
        Assert.Equal("en", recipe.LanguageCode);
    }

    [Fact]
    public async Task ImportAsync_FallsBackToLabeledSectionsInVisibleText()
    {
        var importer = new RecipePageImporter(new HttpClient(new FakeHandler(TextPage)));

        var recipe = await importer.ImportAsync("https://example.com/soup");

        Assert.Equal("Plain soup", recipe.Title);
        Assert.Equal(["4 tomatoes", "1 onion"], recipe.IngredientLines);
        Assert.Equal(["Chop.", "Simmer."], recipe.InstructionLines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/recipe")]
    [InlineData("https://user:pass@example.com/recipe")]
    public async Task ImportAsync_RejectsInvalidAddresses(string address)
    {
        var importer = new RecipePageImporter(new HttpClient(new FakeHandler(TextPage)));

        await Assert.ThrowsAsync<RecipeImportFailedException>(() => importer.ImportAsync(address));
    }

    [Fact]
    public async Task ImportAsync_ReportsPagesWithoutARecipeAndFailedDownloads()
    {
        var empty = new RecipePageImporter(new HttpClient(new FakeHandler("<html><body><h1>Hi</h1></body></html>")));
        await Assert.ThrowsAsync<RecipeImportFailedException>(() => empty.ImportAsync("https://example.com"));

        var notFound = new RecipePageImporter(new HttpClient(new FakeHandler("", HttpStatusCode.NotFound)));
        var error = await Assert.ThrowsAsync<RecipeImportFailedException>(
            () => notFound.ImportAsync("https://example.com"));
        Assert.Contains("404", error.Message);

        var json = new RecipePageImporter(new HttpClient(new FakeHandler("{}", contentType: "application/json")));
        await Assert.ThrowsAsync<RecipeImportFailedException>(() => json.ImportAsync("https://example.com"));
    }

    [Fact]
    public async Task ImportAsync_DetectsSwedishRecipes()
    {
        const string page = """
            <html><head><title>Köttbullar</title></head><body><h1>Köttbullar</h1>
            <h2>Ingredienser</h2><ul><li>500 g köttfärs</li><li>1 ägg</li><li>smör och mjölk</li></ul>
            <h2>Gör så här</h2><ol><li>Blanda och stek.</li></ol></body></html>
            """;
        var importer = new RecipePageImporter(new HttpClient(new FakeHandler(page)));

        var recipe = await importer.ImportAsync("https://example.se/kottbullar");

        Assert.Equal("sv", recipe.LanguageCode);
    }

    private sealed class FakeHandler(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "text/html") : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
                RequestMessage = request
            });
        }
    }
}
