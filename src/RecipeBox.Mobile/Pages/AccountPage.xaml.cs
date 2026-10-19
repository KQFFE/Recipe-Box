namespace RecipeBox.Mobile;

public partial class AccountPage : ContentPage
{
    private readonly ProfileSession session;
    private bool isBusy;

    public AccountPage(ProfileSession session)
    {
        InitializeComponent();
        this.session = session;
#if DEBUG
        DevServerSection.IsVisible = true;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await session.RestoreAsync();
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(exception.Message);
        }

#if DEBUG
        ServerEntry.Text ??= session.SavedServerAddress?.AbsoluteUri;
#endif
        UpdateView();
    }

    private void UpdateView()
    {
        var signedIn = session.IsSignedIn;
        SummaryLabel.Text = signedIn
            ? "You're signed in. Your recipes are saved to your profile and available on your other devices."
            : "Your recipes already work on this device without an account. Sign in only to save them to a profile and use them on other devices.";
        SignInButton.IsVisible = !signedIn;
        SyncButton.IsVisible = signedIn;
        SignOutButton.IsVisible = signedIn;
#if DEBUG
        ChangeServerButton.IsVisible = true;
#endif
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            var serverAddress = GetServerAddress();
            if (serverAddress is null)
            {
                return;
            }

            ShowStatus("Opening secure sign-in in your browser...");
            ShowStatus(await session.SignInAsync(serverAddress));
        });
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            ShowStatus(await session.SyncAsync() ?? "Your recipes are synced with your profile.");
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync(
                "Sign out?",
                "Profile recipes and unsynced profile changes will be removed from this device. Device-only recipes will stay.",
                "Sign out",
                "Cancel"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await session.SignOutAsync();
            ShowStatus("You have been signed out. Device-only recipes remain on this device.");
        });
    }

    private async void OnChangeServerClicked(object? sender, EventArgs e)
    {
#if DEBUG
        var serverAddress = GetServerAddress();
        if (serverAddress is null
            || !await DisplayAlertAsync(
                "Change development server?",
                "This signs you out and removes profile recipes from this device. They remain on the server and can be downloaded again.",
                "Change server",
                "Cancel"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await session.ChangeDevelopmentServerAsync(serverAddress);
            ShowStatus("Development server changed. Sign in again to sync.");
        });
#else
        await Task.CompletedTask;
#endif
    }

    private Uri? GetServerAddress()
    {
#if DEBUG
        if (ProfileSession.TryNormalizeServerAddress(ServerEntry.Text ?? string.Empty, out var address))
        {
            return address;
        }

        ShowStatus("Enter a valid HTTPS server address.");
        return null;
#else
        var address = MobileApiClient.GetConfiguredServerAddress();
        if (address is null)
        {
            ShowStatus("Profile sync is not configured for this app build yet.");
        }

        return address;
#endif
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        SignInButton.IsEnabled = SyncButton.IsEnabled = SignOutButton.IsEnabled = ChangeServerButton.IsEnabled = false;
        try
        {
            await operation();
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(await session.DescribeFailureAsync(exception));
        }
        finally
        {
            isBusy = false;
            SignInButton.IsEnabled = SyncButton.IsEnabled = SignOutButton.IsEnabled = ChangeServerButton.IsEnabled = true;
            UpdateView();
        }
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = true;
    }
}
