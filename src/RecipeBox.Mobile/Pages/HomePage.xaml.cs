namespace RecipeBox.Mobile;

public partial class HomePage : ContentPage
{
    private readonly ProfileSession session;

    public HomePage(ProfileSession session)
    {
        InitializeComponent();
        this.session = session;
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
            // Home stays usable; the account page reports sign-in problems.
        }

        AccountButton.IsVisible = !session.IsSignedIn;
    }

    private async void OnOpenRecipesClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//recipes");

    private async void OnAccountClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//account");
}
