namespace RecipeBox.Mobile;

public partial class ImportPage : ContentPage
{
    private readonly MobileApiClient apiClient;
    private readonly ProfileSession session;
    private readonly RecipeDraftState draftState;
    private bool isBusy;

    public ImportPage(
        MobileApiClient apiClient,
        ProfileSession session,
        RecipeDraftState draftState)
    {
        InitializeComponent();
        this.apiClient = apiClient;
        this.session = session;
        this.draftState = draftState;
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

        AccountPanel.IsVisible = !session.IsSignedIn;
        UrlEntry.IsEnabled = session.IsSignedIn;
        ImportButton.IsEnabled = session.IsSignedIn;
    }

    private async void OnAccountClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//account");

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        if (isBusy || !session.IsSignedIn || session.SavedServerAddress is not Uri serverAddress)
        {
            return;
        }

        isBusy = true;
        ImportButton.IsEnabled = false;
        StatusBorder.IsVisible = false;
        try
        {
            draftState.Pending = await apiClient.ImportRecipeDraftAsync(
                serverAddress,
                UrlEntry.Text ?? string.Empty);
            UrlEntry.Text = string.Empty;
            await Shell.Current.GoToAsync(nameof(RecipeFormPage));
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(await session.DescribeFailureAsync(exception));
            AccountPanel.IsVisible = !session.IsSignedIn;
        }
        finally
        {
            isBusy = false;
            ImportButton.IsEnabled = session.IsSignedIn;
        }
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = true;
    }
}
