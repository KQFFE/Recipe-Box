using System.Text.Json;

namespace RecipeBox.Mobile;

public sealed class MobileTokenStore
{
    private const string TokenStorageKey = "mobile.oauth-session";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(
        Uri serverAddress,
        string accessToken,
        DateTimeOffset expiresAtUtc,
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = new StoredSession(serverAddress.AbsoluteUri, accessToken, expiresAtUtc, refreshToken);
        await SecureStorage.Default.SetAsync(
            TokenStorageKey,
            JsonSerializer.Serialize(session, JsonOptions));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<StoredSession?> GetSessionAsync(
        Uri serverAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storedSession = await SecureStorage.Default.GetAsync(TokenStorageKey);
        cancellationToken.ThrowIfCancellationRequested();
        if (storedSession is null)
        {
            return null;
        }

        var session = JsonSerializer.Deserialize<StoredSession>(storedSession, JsonOptions)
            ?? throw new JsonException("The saved sign-in session is invalid.");
        if (!string.Equals(session.ServerAddress, serverAddress.AbsoluteUri, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            await ClearAsync(cancellationToken);
            return null;
        }

        return session;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(TokenStorageKey);
        return Task.CompletedTask;
    }

    public sealed record StoredSession(
        string ServerAddress,
        string AccessToken,
        DateTimeOffset ExpiresAtUtc,
        string? RefreshToken = null);
}
