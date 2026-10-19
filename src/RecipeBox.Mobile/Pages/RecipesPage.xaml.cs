using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public sealed class RecipeCardItem(StoredRecipe recipe)
{
    public string LocalId { get; } = recipe.LocalId;

    public string Title { get; } = recipe.Title;

    public string? Description { get; } = recipe.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public ImageSource? Image { get; } =
        recipe.CoverImagePath is string path && File.Exists(path) ? ImageSource.FromFile(path) : null;

    public bool HasImage => Image is not null;
}

public partial class RecipesPage : ContentPage
{
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSession session;

    public RecipesPage(LocalRecipeStore recipeStore, ProfileSession session)
    {
        InitializeComponent();
        this.recipeStore = recipeStore;
        this.session = session;
        BindingContext = this;
    }

    public IReadOnlyList<RecipeCardItem> Recipes { get; private set; } = [];

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        string? message = null;
        try
        {
            await session.RestoreAsync();
            await ShowRecipesAsync();
            message = await session.SyncAsync();
            await ShowRecipesAsync();
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            message = await session.DescribeFailureAsync(exception);
        }

        StatusLabel.Text = message ?? string.Empty;
        StatusBorder.IsVisible = message is not null;
    }

    private async Task ShowRecipesAsync()
    {
        var recipes = await recipeStore.GetRecipesAsync();
        Recipes = recipes.Select(recipe => new RecipeCardItem(recipe)).ToList();
        OnPropertyChanged(nameof(Recipes));
        EmptyPanel.IsVisible = recipes.Count == 0;
    }

    private async void OnAddClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(RecipeFormPage));

    private async void OnCaptureClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//capture");

    private async void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: RecipeCardItem item })
        {
            await Shell.Current.GoToAsync($"{nameof(RecipeDetailsPage)}?id={Uri.EscapeDataString(item.LocalId)}");
        }
    }
}
