using System.Net;
using System.Text.Json;
using RecipeBox.Mobile.Core;
using SQLite;

namespace RecipeBox.Mobile;

public sealed class ProfileSession(
    MobileApiClient apiClient,
    LocalRecipeStore recipeStore,
    ProfileSyncService profileSync)
{
    private bool restored;

    public bool IsSignedIn { get; private set; }

    public event EventHandler? Changed;

    public Uri? SavedServerAddress
    {
        get
        {
#if !DEBUG
            return MobileApiClient.GetConfiguredServerAddress();
#else
            var savedAddress = Preferences.Default.Get(MobileApiClient.ServerAddressPreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(savedAddress))
            {
                return MobileApiClient.GetConfiguredServerAddress();
            }

            return TryNormalizeServerAddress(savedAddress, out var serverAddress) ? serverAddress : null;
#endif
        }
    }

    public async Task RestoreAsync()
    {
        if (restored)
        {
            return;
        }

        restored = true;
        var serverAddress = SavedServerAddress;
        if (serverAddress is not null && await apiClient.HasStoredSessionAsync(serverAddress))
        {
            SetSignedIn(true);
        }
    }

    public async Task<string> SignInAsync(Uri serverAddress)
    {
        Preferences.Default.Set(MobileApiClient.ServerAddressPreferenceKey, serverAddress.AbsoluteUri);
        if (!await apiClient.HasStoredSessionAsync(serverAddress))
        {
            try
            {
                await apiClient.SignInAsync(serverAddress);
            }
            catch (OperationCanceledException)
            {
                return "Sign-in was not completed, so nothing was synced. "
                    + "If you just created an account, confirm it in the browser, return to this app, and sign in again. "
                    + "Your recipes are still on this device.";
            }
        }

        SetSignedIn(true);
        await profileSync.SyncAsync(serverAddress);
        return "You're signed in and your recipes are synced with your profile.";
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (SavedServerAddress is Uri serverAddress)
            {
                await apiClient.SignOutAsync(serverAddress);
            }
        }
        finally
        {
            await recipeStore.RemoveProfileRecipesAsync();
            SetSignedIn(false);
        }
    }

    public async Task<string?> SyncAsync()
    {
        if (!IsSignedIn || SavedServerAddress is not Uri serverAddress)
        {
            return null;
        }

        try
        {
            await profileSync.SyncAsync(serverAddress);
            return null;
        }
        catch (Exception exception) when (IsExpectedClientException(exception))
        {
            return await DescribeFailureAsync(exception);
        }
    }

    public async Task<string?> SyncAfterLocalChangeAsync()
    {
        var failure = await SyncAsync();
        return failure is null ? null : $"Saved on this device. Profile sync failed: {failure}";
    }

    public async Task<string> DescribeFailureAsync(Exception exception)
    {
        if (exception is MobileApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            await apiClient.ClearLocalSessionAsync();
            await recipeStore.RemoveProfileRecipesAsync();
            SetSignedIn(false);
            return "Your profile session expired. Sign in again to keep syncing. Device-only recipes are still here.";
        }

        return exception.Message;
    }

#if DEBUG
    public async Task ChangeDevelopmentServerAsync(Uri serverAddress)
    {
        await apiClient.ClearLocalSessionAsync();
        await recipeStore.RemoveProfileRecipesAsync();
        Preferences.Default.Set(MobileApiClient.ServerAddressPreferenceKey, serverAddress.AbsoluteUri);
        SetSignedIn(false);
    }
#endif

    public static bool TryNormalizeServerAddress(string value, out Uri serverAddress)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsedAddress)
            && parsedAddress.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedAddress.Host)
            && string.IsNullOrEmpty(parsedAddress.UserInfo)
            && string.IsNullOrEmpty(parsedAddress.Query)
            && string.IsNullOrEmpty(parsedAddress.Fragment))
        {
            serverAddress = new Uri(parsedAddress.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
            return true;
        }

        serverAddress = null!;
        return false;
    }

    public static bool IsExpectedClientException(Exception exception) =>
        exception is MobileApiException
            or HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or FeatureNotSupportedException
            or PermissionException
            or JsonException
            or SQLiteException
            or OperationCanceledException;

    private void SetSignedIn(bool signedIn)
    {
        if (IsSignedIn == signedIn)
        {
            return;
        }

        IsSignedIn = signedIn;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
