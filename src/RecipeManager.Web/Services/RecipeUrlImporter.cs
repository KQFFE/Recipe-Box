using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Services;

public sealed class RecipeUrlImporter(HttpClient httpClient) : IRecipeUrlImporter
{
    private const int MaximumHtmlBytes = 2 * 1024 * 1024;

    public async Task<RecipeDraft> ImportAsync(Uri recipeUri, CancellationToken cancellationToken)
    {
        ValidateRecipeUri(recipeUri);

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
            throw new RecipeImportException("The address did not return an HTML recipe page.");
        }

        if (response.Content.Headers.ContentLength is > MaximumHtmlBytes)
        {
            throw new RecipeImportException("The recipe page is too large to import (maximum 2 MB).");
        }

        var html = await ReadHtmlAsync(response.Content, cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri ?? recipeUri;
        return RecipeStructuredDataParser.Parse(html, finalUri)
            ?? throw new RecipeImportException(
                "No supported recipe data was found on that page. Try a page with Schema.org Recipe structured data.");
    }

    internal static void ValidateRecipeUri(Uri recipeUri)
    {
        ArgumentNullException.ThrowIfNull(recipeUri);

        if (!recipeUri.IsAbsoluteUri
            || (recipeUri.Scheme != Uri.UriSchemeHttp && recipeUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(recipeUri.Host)
            || !recipeUri.IsDefaultPort
            || !string.IsNullOrEmpty(recipeUri.UserInfo)
            || recipeUri.AbsoluteUri.Length > 2048
            || IPAddress.TryParse(recipeUri.Host, out _))
        {
            throw new RecipeImportException("Enter a valid public HTTP or HTTPS recipe page URL.");
        }
    }

    private static async Task<string> ReadHtmlAsync(
        HttpContent content,
        CancellationToken cancellationToken)
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
                throw new RecipeImportException("The recipe page is too large to import (maximum 2 MB).");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    public static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !PublicInternetAddress.IsPublic(address)))
        {
            throw new HttpRequestException("The recipe site does not resolve to a public Internet address.");
        }

        SocketException? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastException = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to the recipe site.", lastException);
    }
}
