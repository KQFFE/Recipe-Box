using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public sealed class MobileApiClient(HttpClient httpClient, MobileTokenStore tokenStore) : IRecipeProfileApi
{
    public const string ServerAddressPreferenceKey = "mobile.server-address";
    private const string ClientId = "recipebox-mobile";
    private const string RedirectUri = "com.kqffe.recipebox://callback";
    private const string RequestedScopes = "openid profile email offline_access recipes.read recipes.write";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public static Uri? GetConfiguredServerAddress()
    {
        var configuredAddress = typeof(MobileApiClient).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RecipeBoxServerUrl")
            ?.Value;
        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out var parsedAddress)
            || parsedAddress.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(parsedAddress.Host)
            || !string.IsNullOrEmpty(parsedAddress.UserInfo)
            || !string.IsNullOrEmpty(parsedAddress.Query)
            || !string.IsNullOrEmpty(parsedAddress.Fragment))
        {
            return null;
        }

        return new Uri(parsedAddress.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
    }

    public async Task SignInAsync(Uri serverAddress, CancellationToken cancellationToken = default)
    {
        var verifier = CreateRandomUrlSafeValue();
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = CreateRandomUrlSafeValue();
        var authorizationUri = new Uri(
            serverAddress,
            "connect/authorize"
            + $"?client_id={Uri.EscapeDataString(ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(RequestedScopes)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256"
            + $"&state={Uri.EscapeDataString(state)}");

        var result = await WebAuthenticator.Default.AuthenticateAsync(
            new WebAuthenticatorOptions
            {
                Url = authorizationUri,
                CallbackUrl = new Uri(RedirectUri)
            },
            cancellationToken);

        if (result.Properties.TryGetValue("error", out var error))
        {
            var description = result.Properties.TryGetValue("error_description", out var errorDescription)
                ? errorDescription
                : error;
            throw new InvalidOperationException($"Sign-in failed: {description}");
        }

        if (!result.Properties.TryGetValue("state", out var returnedState)
            || !string.Equals(state, returnedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Sign-in could not be verified. Please try again.");
        }

        if (!result.Properties.TryGetValue("code", out var authorizationCode)
            || string.IsNullOrWhiteSpace(authorizationCode))
        {
            throw new InvalidOperationException("The sign-in server did not return an authorization code.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serverAddress, "connect/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["code"] = authorizationCode,
                ["code_verifier"] = verifier
            })
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateOAuthException(response.StatusCode, responseBody);
        }

        using var tokenDocument = JsonDocument.Parse(responseBody);
        var root = tokenDocument.RootElement;
        await SaveTokenResponseAsync(serverAddress, responseBody, null, cancellationToken);
    }

    public async Task<bool> HasStoredSessionAsync(
        Uri serverAddress,
        CancellationToken cancellationToken = default) =>
        await tokenStore.GetSessionAsync(serverAddress, cancellationToken) is not null;

    public async Task<RecipeListResponse> GetRecipesAsync(
        Uri serverAddress,
        int page,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            new Uri(serverAddress, $"api/v1/recipes?page={page}&pageSize=20"),
            serverAddress,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var recipeList = await response.Content.ReadFromJsonAsync<RecipeListResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty response.");
        return recipeList;
    }

    public async Task<RecipeResponse> GetRecipeAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}"),
            serverAddress,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty recipe response.");
    }

    public async Task<RecipeResponse> CreateRecipeAsync(
        Uri serverAddress,
        RecipeWriteRequest recipe,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            new Uri(serverAddress, "api/v1/recipes"),
            serverAddress,
            cancellationToken);
        request.Content = JsonContent.Create(recipe, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty create response.");
    }

    public async Task<RecipeResponse> UpdateRecipeAsync(
        Uri serverAddress,
        int recipeId,
        RecipeWriteRequest recipe,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Put,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}"),
            serverAddress,
            cancellationToken);
        request.Content = JsonContent.Create(recipe, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecipeResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty update response.");
    }

    public async Task DeleteRecipeAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Delete,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}"),
            serverAddress,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RecipeImageResponse?> GetRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}/image"),
            serverAddress,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType
            ?? throw new JsonException("The recipe server did not specify the cover image type.");
        return new RecipeImageResponse(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            contentType);
    }

    public async Task UploadRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        byte[] imageBytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}/image"),
            serverAddress,
            cancellationToken);
        using var multipart = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(imageContent, "image", fileName);
        request.Content = multipart;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteRecipeImageAsync(
        Uri serverAddress,
        int recipeId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Delete,
            new Uri(serverAddress, $"api/v1/recipes/{recipeId}/image"),
            serverAddress,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RecipeDraftResponse> ExtractPhotoDraftAsync(
        Uri serverAddress,
        byte[] photoBytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            new Uri(serverAddress, "api/v1/recipes/drafts/photo"),
            serverAddress,
            cancellationToken);
        using var multipart = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(photoBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(imageContent, "photo", fileName);
        request.Content = multipart;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecipeDraftResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty photo-draft response.");
    }

    public async Task<RecipeDraftResponse> ImportRecipeDraftAsync(
        Uri serverAddress,
        string recipeUrl,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            new Uri(serverAddress, "api/v1/recipes/drafts/import"),
            serverAddress,
            cancellationToken);
        request.Content = JsonContent.Create(new RecipeDraftRequest(recipeUrl), options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RecipeDraftResponse>(JsonOptions, cancellationToken)
            ?? throw new JsonException("The recipe server returned an empty import-draft response.");
    }

    public async Task SignOutAsync(
        Uri serverAddress,
        CancellationToken cancellationToken = default)
    {
        var session = await tokenStore.GetSessionAsync(serverAddress, cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(session?.RefreshToken))
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri(serverAddress, "connect/revoke"))
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = ClientId,
                        ["token"] = session.RefreshToken,
                        ["token_type_hint"] = "refresh_token"
                    })
                };
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw CreateOAuthException(response.StatusCode, responseBody);
                }
            }
        }
        finally
        {
            await tokenStore.ClearAsync(CancellationToken.None);
        }
    }

    public Task ClearLocalSessionAsync(CancellationToken cancellationToken = default) =>
        tokenStore.ClearAsync(cancellationToken);

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        HttpMethod method,
        Uri requestUri,
        Uri serverAddress,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(serverAddress, cancellationToken);
        if (accessToken is null)
        {
            throw new MobileApiException(
                HttpStatusCode.Unauthorized,
                "Your sign-in has expired. Please sign in again.");
        }

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<string?> GetAccessTokenAsync(
        Uri serverAddress,
        CancellationToken cancellationToken)
    {
        var session = await tokenStore.GetSessionAsync(serverAddress, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return session.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(session.RefreshToken))
        {
            await tokenStore.ClearAsync(cancellationToken);
            return null;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            session = await tokenStore.GetSessionAsync(serverAddress, cancellationToken);
            if (session is null)
            {
                return null;
            }

            if (session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return session.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(session.RefreshToken))
            {
                await tokenStore.ClearAsync(cancellationToken);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serverAddress, "connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = ClientId,
                    ["refresh_token"] = session.RefreshToken
                })
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    await tokenStore.ClearAsync(cancellationToken);
                }

                throw CreateOAuthException(response.StatusCode, responseBody);
            }

            await SaveTokenResponseAsync(
                serverAddress,
                responseBody,
                session.RefreshToken,
                cancellationToken);

            var refreshedSession = await tokenStore.GetSessionAsync(serverAddress, cancellationToken);
            return refreshedSession?.AccessToken
                ?? throw new JsonException("The refreshed sign-in session could not be loaded.");
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task SaveTokenResponseAsync(
        Uri serverAddress,
        string responseBody,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        using var tokenDocument = JsonDocument.Parse(responseBody);
        var root = tokenDocument.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(accessToken)
            || !root.TryGetProperty("token_type", out var tokenType)
            || !string.Equals(tokenType.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !root.TryGetProperty("expires_in", out var expiresInElement)
            || !expiresInElement.TryGetInt64(out var expiresInSeconds)
            || expiresInSeconds <= 0)
        {
            throw new JsonException("The sign-in server returned an invalid access token response.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : previousRefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new JsonException("The sign-in server did not issue a refresh token.");
        }

        await tokenStore.SaveAsync(
            serverAddress,
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            refreshToken,
            cancellationToken);
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokenStore.ClearAsync(cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryGetApiErrorMessage(responseBody)
            ?? $"The recipe server returned HTTP {(int)response.StatusCode}.";
        throw new MobileApiException(response.StatusCode, message);
    }

    private static string? TryGetApiErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var messages = new List<string>();
        if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
        {
            messages.Add(detail.GetString()!);
        }
        else if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
        {
            messages.Add(title.GetString()!);
        }

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
        {
            messages.AddRange(errors.EnumerateObject()
                .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()!)
                    : []));
        }

        return messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
    }

    private static MobileApiException CreateOAuthException(HttpStatusCode statusCode, string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var message = root.TryGetProperty("error_description", out var description)
            ? description.GetString()
            : root.TryGetProperty("error", out var error)
                ? error.GetString()
                : null;
        return new MobileApiException(
            statusCode,
            message ?? $"The sign-in server returned HTTP {(int)statusCode}.");
    }

    private static string CreateRandomUrlSafeValue() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

public sealed record RecipeDraftRequest(string Url);

public sealed record RecipeDraftResponse(
    string Title,
    string? Description,
    string? ImageUrl,
    string LanguageCode,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions);

public sealed class MobileApiException(HttpStatusCode statusCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
