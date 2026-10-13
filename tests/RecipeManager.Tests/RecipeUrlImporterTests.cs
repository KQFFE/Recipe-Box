using System.Net;
using System.Net.Http.Headers;
using RecipeManager.Web.Models;
using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class RecipeUrlImporterTests
{
    [Fact]
    public void StructuredDataParser_ParsesRecipeFromGraphAndResolvesImageUrl()
    {
        const string html = """
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "Organization", "name": "Recipe site" },
                {
                  "@type": ["Thing", "https://schema.org/Recipe"],
                  "name": "Tomato soup",
                  "description": "A simple soup.",
                  "image": { "url": "/images/tomato-soup.jpg" },
                  "recipeIngredient": ["4 tomatoes", "1 onion"],
                  "recipeInstructions": [
                    { "@type": "HowToStep", "text": "Roast the vegetables." },
                    {
                      "@type": "HowToSection",
                      "itemListElement": [
                        { "@type": "HowToStep", "text": "Blend until smooth." }
                      ]
                    }
                  ]
                }
              ]
            }
            </script>
            """;

        var draft = RecipeStructuredDataParser.Parse(
            html,
            new Uri("https://recipes.example.test/recipes/tomato-soup"));

        Assert.NotNull(draft);
        Assert.Equal("Tomato soup", draft.Title);
        Assert.Equal("A simple soup.", draft.Description);
        Assert.Equal($"4 tomatoes{Environment.NewLine}1 onion", draft.Ingredients);
        Assert.Equal($"Roast the vegetables.{Environment.NewLine}Blend until smooth.", draft.Instructions);
        Assert.Equal("https://recipes.example.test/images/tomato-soup.jpg", draft.ImageUrl);
    }

    [Fact]
    public void StructuredDataParser_SkipsMalformedAndNonRecipeJsonLdBlocks()
    {
        const string html = """
            <script type="application/ld+json">{invalid json}</script>
            <script type='application/ld+json; charset=utf-8'>
                {"@type":"WebPage","name":"A page"}
            </script>
            <script type="application/ld+json">
                {"@type":"Recipe","name":"Toast","recipeIngredient":"1 slice bread",
                 "recipeInstructions":"Toast the bread."}
            </script>
            """;

        var draft = RecipeStructuredDataParser.Parse(html, new Uri("https://recipes.example.test/toast"));

        Assert.NotNull(draft);
        Assert.Equal("Toast", draft.Title);
        Assert.Equal("1 slice bread", draft.Ingredients);
        Assert.Equal("Toast the bread.", draft.Instructions);
    }

    [Fact]
    public void StructuredDataParser_ReturnsNullWhenPageHasNoRecipe()
    {
        var draft = RecipeStructuredDataParser.Parse(
            """<script type="application/ld+json">{"@type":"WebPage"}</script>""",
            new Uri("https://recipes.example.test"));

        Assert.Null(draft);
    }

    [Fact]
    public async Task Importer_RejectsUnsupportedUriBeforeSendingRequest()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("No request expected."));
        var importer = new RecipeUrlImporter(new HttpClient(handler));

        await Assert.ThrowsAsync<RecipeImportException>(() =>
            importer.ImportAsync(new Uri("http://127.0.0.1/recipe"), CancellationToken.None));
    }

    [Fact]
    public async Task Importer_FetchesAndParsesRecipeHtml()
    {
        const string html = """
            <html><script type="application/ld+json">
            {"@type":"Recipe","name":"Toast","recipeIngredient":["1 slice bread"],
             "recipeInstructions":[{"@type":"HowToStep","text":"Toast the bread."}]}
            </script></html>
            """;
        HttpRequestMessage? requestSeen = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            requestSeen = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        });
        var importer = new RecipeUrlImporter(new HttpClient(handler));

        var draft = await importer.ImportAsync(
            new Uri("https://recipes.example.test/toast"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, requestSeen?.Method);
        Assert.Equal("text/html", requestSeen?.Headers.Accept.Single().MediaType);
        Assert.Equal("Toast", draft.Title);
        Assert.Equal("1 slice bread", draft.Ingredients);
        Assert.Equal("Toast the bread.", draft.Instructions);
    }

    [Fact]
    public async Task Importer_RejectsNonHtmlResponses()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        });
        var importer = new RecipeUrlImporter(new HttpClient(handler));

        await Assert.ThrowsAsync<RecipeImportException>(() =>
            importer.ImportAsync(new Uri("https://recipes.example.test/recipe"), CancellationToken.None));
    }

    [Fact]
    public async Task Importer_EnforcesHtmlLimitWhileReadingResponse()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', 2 * 1024 * 1024 + 1))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        });
        var importer = new RecipeUrlImporter(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<RecipeImportException>(() =>
            importer.ImportAsync(new Uri("https://recipes.example.test/recipe"), CancellationToken.None));

        Assert.Contains("maximum 2 MB", exception.Message);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

public sealed class PublicInternetAddressTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:0808:0808::1", false)]
    [InlineData("3fff::1", false)]
    public void IsPublic_RejectsNonPublicAddressRanges(string address, bool expected)
    {
        Assert.Equal(expected, PublicInternetAddress.IsPublic(IPAddress.Parse(address)));
    }
}
