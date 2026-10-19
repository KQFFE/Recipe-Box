using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public partial class SharePage : ContentPage, IQueryAttributable
{
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSession session;
    private readonly MobileApiClient apiClient;
    private string? recipeId;
    private string? shareUrl;
    private bool isBusy;

    public SharePage(LocalRecipeStore recipeStore, ProfileSession session, MobileApiClient apiClient)
    {
        InitializeComponent();
        this.recipeStore = recipeStore;
        this.session = session;
        this.apiClient = apiClient;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value))
        {
            recipeId = value as string;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        shareUrl = null;
        ShowLink();
        var recipe = recipeId is null ? null : await recipeStore.GetByLocalIdAsync(recipeId);
        if (recipe is null || recipe.IsDeleted)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        HeadingLabel.Text = recipe.Title;
        if (!session.IsSignedIn)
        {
            ShowStatus("Sharing needs an account and a network connection. Sign in from the Account tab first.");
            CreateButton.IsEnabled = false;
            return;
        }

        await RunAsync(async serverId =>
        {
            var status = await apiClient.GetShareStatusAsync(RequireServer(), serverId);
            ShowStatus(status.Active
                ? "This recipe is shared. For security the link is only shown when it is created; create a new link to copy it again."
                : null);
            RevokeButton.IsVisible = status.Active;
        });
    }

    private async void OnCreateClicked(object? sender, EventArgs e) =>
        await RunAsync(async serverId =>
        {
            var share = await apiClient.CreateShareLinkAsync(RequireServer(), serverId);
            shareUrl = share.Url;
            ShowLink();
        });

    private async void OnRevokeClicked(object? sender, EventArgs e) =>
        await RunAsync(async serverId =>
        {
            await apiClient.RevokeShareLinkAsync(RequireServer(), serverId);
            shareUrl = null;
            ShowLink();
            RevokeButton.IsVisible = false;
            ShowStatus("Sharing stopped. The old link no longer works.");
        });

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (shareUrl is not null)
        {
            await Clipboard.Default.SetTextAsync(shareUrl);
            ShowStatus("Link copied.");
        }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (shareUrl is not null)
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Uri = shareUrl,
                Title = HeadingLabel.Text,
            });
        }
    }

    private Uri RequireServer() =>
        session.SavedServerAddress ?? throw new InvalidOperationException("No recipe server is configured.");

    // Runs a share call for the recipe, syncing first so it has a server id.
    private async Task RunAsync(Func<int, Task> action)
    {
        if (isBusy || recipeId is null)
        {
            return;
        }

        isBusy = true;
        Spinner.IsVisible = Spinner.IsRunning = true;
        try
        {
            var recipe = await recipeStore.GetByLocalIdAsync(recipeId);
            if (recipe?.ServerId is null)
            {
                var failure = await session.SyncAsync();
                recipe = await recipeStore.GetByLocalIdAsync(recipeId);
                if (recipe?.ServerId is null)
                {
                    ShowStatus(failure ?? "This recipe has not synced to your profile yet. Try again in a moment.");
                    return;
                }
            }

            await action(recipe.ServerId.Value);
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(await session.DescribeFailureAsync(exception));
        }
        finally
        {
            Spinner.IsVisible = Spinner.IsRunning = false;
            isBusy = false;
        }
    }

    private void ShowLink()
    {
        var hasLink = shareUrl is not null;
        LinkLabel.Text = shareUrl;
        LinkBorder.IsVisible = hasLink;
        CopyButton.IsVisible = hasLink;
        ShareButton.IsVisible = hasLink;
        RevokeButton.IsVisible = hasLink || RevokeButton.IsVisible;
        CreateButton.Text = hasLink ? "Create new link" : "Create share link";
    }

    private void ShowStatus(string? message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = !string.IsNullOrEmpty(message);
    }
}
