using System.Net.Http.Headers;
using System.Text;

namespace RecipeBox.Mobile.Core;

// Downloads a public recipe page on the device and reads the recipe from it. No account or server involved.
public sealed class RecipePageImporter(HttpClient httpClient)
{
    private const int MaximumHtmlBytes = 2 * 1024 * 1024;

    public async Task<ImportedRecipe> ImportAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!TryParseUri(address, out var recipeUri))
        {
            throw new RecipeImportFailedException("Enter a valid public HTTP or HTTPS recipe page URL.");
        }

        string html;
        Uri finalUri;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, recipeUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.UserAgent.ParseAdd("RecipeBox/1.0");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null
                || (!mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                    && !mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            {
                throw new RecipeImportFailedException("The address did not return an HTML recipe page.");
            }

            if (response.Content.Headers.ContentLength is > MaximumHtmlBytes)
            {
                throw new RecipeImportFailedException("The recipe page is too large to import (maximum 2 MB).");
            }

            html = await ReadHtmlAsync(response.Content, cancellationToken);
            finalUri = response.RequestMessage?.RequestUri ?? recipeUri;
        }
        catch (HttpRequestException exception)
        {
            throw new RecipeImportFailedException(
                "The recipe page could not be downloaded. Check your internet connection and the address."
                + (exception.StatusCode is { } status ? $" (The site answered {(int)status}.)" : string.Empty));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RecipeImportFailedException("The recipe page took too long to respond.");
        }

        var recipe = StructuredRecipeParser.Parse(html, finalUri) ?? PageTextRecipeParser.Parse(html, finalUri);
        if (recipe is null
            || string.IsNullOrWhiteSpace(recipe.Title)
            || string.IsNullOrWhiteSpace(recipe.Ingredients)
            || string.IsNullOrWhiteSpace(recipe.Instructions))
        {
            throw new RecipeImportFailedException(
                "Recipe Box couldn't find a recipe title with both ingredient and instruction sections in the page. Try a printer-friendly recipe page or enter the recipe by hand.");
        }

        return recipe;
    }

    public static bool TryParseUri(string? value, out Uri recipeUri)
    {
        recipeUri = null!;
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // Addresses typed without a scheme, such as "example.com/recipe", are treated as HTTPS.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || parsed.AbsoluteUri.Length > 2048)
        {
            return false;
        }

        recipeUri = parsed;
        return true;
    }

    private static async Task<string> ReadHtmlAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (output.Length + bytesRead > MaximumHtmlBytes)
            {
                throw new RecipeImportFailedException("The recipe page is too large to import (maximum 2 MB).");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
