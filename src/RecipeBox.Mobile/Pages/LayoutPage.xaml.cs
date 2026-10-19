using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public partial class LayoutPage : ContentPage, IQueryAttributable
{
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSession session;
    private string? recipeId;
    private StoredRecipe? recipe;
    private RecipeLanguage language = RecipeLanguages.Get(null);
    private List<string> order = [];
    private HashSet<string> fullWidth = new(StringComparer.Ordinal);
    private bool hasImage;
    private bool isBusy;

    public LayoutPage(LocalRecipeStore recipeStore, ProfileSession session)
    {
        InitializeComponent();
        this.recipeStore = recipeStore;
        this.session = session;
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
        recipe = recipeId is null ? null : await recipeStore.GetByLocalIdAsync(recipeId);
        if (recipe is null || recipe.IsDeleted)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        language = RecipeLanguages.Get(recipe.LanguageCode);
        HeadingLabel.Text = recipe.Title;
        hasImage = await recipeStore.ReadCoverImageAsync(recipe) is not null;
        var settings = RecipeLayout.Resolve(recipe, hasImage);
        order = settings.Order.ToList();
        fullWidth = settings.FullWidth.ToHashSet(StringComparer.Ordinal);
        Render();
    }

    private void Render()
    {
        SectionList.Children.Clear();
        for (var index = 0; index < order.Count; index++)
        {
            var key = order[index];
            var position = index;
            var up = new Button
            {
                Text = "\u2191",
                IsEnabled = index > 0,
                Style = (Style)Application.Current!.Resources["OutlinePrimaryButton"],
            };
            SemanticProperties.SetDescription(up, $"Move {RecipeLanguages.SectionLabel(language, key)} up");
            up.Clicked += (_, _) => Move(position, -1);

            var down = new Button
            {
                Text = "\u2193",
                IsEnabled = index < order.Count - 1,
                Style = (Style)Application.Current!.Resources["OutlinePrimaryButton"],
            };
            SemanticProperties.SetDescription(down, $"Move {RecipeLanguages.SectionLabel(language, key)} down");
            down.Clicked += (_, _) => Move(position, 1);

            var check = new CheckBox { IsChecked = fullWidth.Contains(key) };
            check.CheckedChanged += (_, args) =>
            {
                if (args.Value)
                {
                    fullWidth.Add(key);
                }
                else
                {
                    fullWidth.Remove(key);
                }
            };
            SemanticProperties.SetDescription(check, $"{RecipeLanguages.SectionLabel(language, key)} full width");

            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                ],
                ColumnSpacing = 8,
            };
            row.Add(new Label
            {
                Text = RecipeLanguages.SectionLabel(language, key),
                Style = (Style)Application.Current!.Resources["CardTitle"],
                VerticalOptions = LayoutOptions.Center,
            });
            row.Add(new HorizontalStackLayout
            {
                Spacing = 4,
                Children = { up, down },
            }, 1);
            row.Add(new HorizontalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    check,
                    new Label { Text = "Full width", VerticalOptions = LayoutOptions.Center },
                },
            }, 2);

            // Place the full-width toggle under the title/move buttons so narrow phones do not clip it.
            var card = new Border
            {
                Style = (Style)Application.Current!.Resources["Card"],
                Padding = 12,
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { row },
                },
            };
            SectionList.Children.Add(card);
        }
    }

    private void Move(int index, int direction)
    {
        var target = index + direction;
        if (target < 0 || target >= order.Count)
        {
            return;
        }

        (order[index], order[target]) = (order[target], order[index]);
        Render();
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        if (recipe is null)
        {
            return;
        }

        var defaults = RecipeLayout.Default(RecipeLayout.AvailableSections(recipe, hasImage));
        order = defaults.Order.ToList();
        fullWidth = defaults.FullWidth.ToHashSet(StringComparer.Ordinal);
        Render();
    }

    private async void OnCancelClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (isBusy || recipe is null)
        {
            return;
        }

        isBusy = true;
        try
        {
            RecipeLayout.Save(recipe, order, fullWidth);
            await recipeStore.SaveLocalAsync(recipe);
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
