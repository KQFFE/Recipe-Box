namespace RecipeBox.Mobile;

public partial class CapturePage : ContentPage
{
    private readonly MobileApiClient apiClient;
    private readonly ProfileSession session;
    private readonly RecipeDraftState draftState;
    private bool isBusy;

    public CapturePage(
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
        CaptureButton.IsEnabled = session.IsSignedIn;
        ChooseButton.IsEnabled = session.IsSignedIn;
    }

    private async void OnAccountClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//account");

    private async void OnCaptureClicked(object? sender, EventArgs e) =>
        await ExtractAsync(() => MediaPicker.Default.CapturePhotoAsync(
            new MediaPickerOptions { Title = "Capture a recipe" }));

    private async void OnChooseClicked(object? sender, EventArgs e) =>
        await ExtractAsync(() => FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a recipe photo",
            FileTypes = FilePickerFileType.Images
        }));

    private async Task ExtractAsync(Func<Task<FileResult?>> pickPhoto)
    {
        if (isBusy || !session.IsSignedIn || session.SavedServerAddress is not Uri serverAddress)
        {
            return;
        }

        isBusy = true;
        StatusBorder.IsVisible = false;
        try
        {
            var photo = await pickPhoto();
            if (photo is null)
            {
                ShowStatus("Photo capture was cancelled.");
                return;
            }

            await using var photoStream = await photo.OpenReadAsync();
            var (bytes, contentType, fileName) = await PhotoReader.ReadAsync(photoStream);
            draftState.Pending = await apiClient.ExtractPhotoDraftAsync(
                serverAddress,
                bytes,
                contentType,
                fileName);
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
        }
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = true;
    }
}
