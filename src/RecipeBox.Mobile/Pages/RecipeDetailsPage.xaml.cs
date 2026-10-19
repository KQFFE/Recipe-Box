using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public sealed record RecipeLine(string Number, string Text);

public partial class RecipeDetailsPage : ContentPage, IQueryAttributable
{
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSession session;
    private string? recipeId;
    private bool isBusy;

    public RecipeDetailsPage(LocalRecipeStore recipeStore, ProfileSession session)
    {
        InitializeComponent();
        this.recipeStore = recipeStore;
        this.session = session;
        BindingContext = this;
    }

    public IReadOnlyList<RecipeLine> Ingredients { get; private set; } = [];

    public IReadOnlyList<RecipeLine> Instructions { get; private set; } = [];

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
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var recipe = recipeId is null ? null : await recipeStore.GetByLocalIdAsync(recipeId);
        if (recipe is null || recipe.IsDeleted)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var language = RecipeLanguages.Get(recipe.LanguageCode);
        Title = recipe.Title;
        TitleLabel.Text = recipe.Title;
        DescriptionLabel.Text = recipe.Description;
        DescriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(recipe.Description);
        IngredientsHeading.Text = language.IngredientsLabel;
        InstructionsHeading.Text = language.InstructionsLabel;
        Ingredients = recipe.Ingredients.Select(line => new RecipeLine(string.Empty, line)).ToList();
        Instructions = recipe.Instructions
            .Select((line, index) => new RecipeLine($"{index + 1}.", line))
            .ToList();
        OnPropertyChanged(nameof(Ingredients));
        OnPropertyChanged(nameof(Instructions));

        var imageBytes = await recipeStore.ReadCoverImageAsync(recipe);
        CoverImage.Source = imageBytes is null
            ? null
            : ImageSource.FromStream(() => new MemoryStream(imageBytes));
        CoverImage.IsVisible = imageBytes is not null;
        SemanticProperties.SetDescription(CoverImage, recipe.Title);
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (recipeId is not null)
        {
            await Shell.Current.GoToAsync($"{nameof(RecipeFormPage)}?id={Uri.EscapeDataString(recipeId)}");
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (isBusy
            || recipeId is not string id
            || !await DisplayAlertAsync(
                "Delete recipe?",
                $"Delete \"{TitleLabel.Text}\" and its cover image? This cannot be undone.",
                "Delete",
                "Cancel"))
        {
            return;
        }

        isBusy = true;
        try
        {
            await recipeStore.DeleteAsync(id);
            await session.SyncAfterLocalChangeAsync();
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            StatusLabel.Text = await session.DescribeFailureAsync(exception);
            StatusBorder.IsVisible = true;
        }
        finally
        {
            isBusy = false;
        }
    }
}
