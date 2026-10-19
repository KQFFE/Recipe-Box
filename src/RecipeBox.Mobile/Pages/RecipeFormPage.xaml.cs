using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public partial class RecipeFormPage : ContentPage, IQueryAttributable
{
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSession session;
    private readonly RecipeDraftState draftState;
    private string? recipeId;
    private bool loaded;
    private bool isBusy;
    private (byte[] Bytes, string ContentType)? pendingImage;
    private bool removeImage;

    public RecipeFormPage(
        LocalRecipeStore recipeStore,
        ProfileSession session,
        RecipeDraftState draftState)
    {
        InitializeComponent();
        this.recipeStore = recipeStore;
        this.session = session;
        this.draftState = draftState;
        LanguagePicker.ItemsSource = RecipeLanguages.All.Select(language => language.Name).ToList();
        LanguagePicker.SelectedIndex = 0;
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
        if (loaded)
        {
            return;
        }

        loaded = true;
        try
        {
            if (recipeId is not null)
            {
                await LoadRecipeAsync(recipeId);
            }
            else if (draftState.Take() is RecipeDraftResponse draft)
            {
                ApplyDraft(draft);
            }
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(exception.Message);
        }

        ApplyLanguageLabels();
    }

    private async Task LoadRecipeAsync(string id)
    {
        var recipe = await recipeStore.GetByLocalIdAsync(id)
            ?? throw new InvalidOperationException("The recipe being edited no longer exists.");
        Title = "Edit recipe";
        EyebrowLabel.Text = "Editing";
        HeadingLabel.Text = recipe.Title;
        SaveButton.Text = "Save changes";
        TitleEntry.Text = recipe.Title;
        DescriptionEditor.Text = recipe.Description;
        LanguagePicker.SelectedIndex = IndexOfLanguage(recipe.LanguageCode);
        IngredientsEditor.Text = string.Join(Environment.NewLine, recipe.Ingredients);
        InstructionsEditor.Text = string.Join(Environment.NewLine, recipe.Instructions);
        var imageBytes = await recipeStore.ReadCoverImageAsync(recipe);
        if (imageBytes is not null)
        {
            ShowCoverImage(imageBytes);
        }
    }

    private void ApplyDraft(RecipeDraftResponse draft)
    {
        TitleEntry.Text = draft.Title;
        DescriptionEditor.Text = draft.Description;
        LanguagePicker.SelectedIndex = IndexOfLanguage(draft.LanguageCode);
        IngredientsEditor.Text = string.Join(Environment.NewLine, draft.Ingredients);
        InstructionsEditor.Text = string.Join(Environment.NewLine, draft.Instructions);
        ShowStatus("Review and edit the extracted recipe, then save it.");
    }

    private static int IndexOfLanguage(string? code)
    {
        var index = RecipeLanguages.All.ToList().FindIndex(language => language.Code == code);
        return index < 0 ? 0 : index;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguageLabels();

    private void ApplyLanguageLabels()
    {
        var language = RecipeLanguages.All[Math.Max(LanguagePicker.SelectedIndex, 0)];
        IngredientsLabel.Text = language.IngredientsLabel;
        InstructionsLabel.Text = language.InstructionsLabel;
        IngredientsHint.Text = language.IngredientsHint;
        InstructionsHint.Text = language.InstructionsHint;
    }

    private async void OnChooseCoverImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var photo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a recipe cover image",
                FileTypes = FilePickerFileType.Images
            });
            if (photo is null)
            {
                return;
            }

            await using var photoStream = await photo.OpenReadAsync();
            var (bytes, contentType, _) = await PhotoReader.ReadAsync(photoStream);
            pendingImage = (bytes, contentType);
            removeImage = false;
            ShowCoverImage(bytes);
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(exception.Message);
        }
    }

    private async void OnRemoveCoverImageClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync(
                "Remove cover image?",
                "The image is removed when you save this recipe.",
                "Remove",
                "Cancel"))
        {
            return;
        }

        pendingImage = null;
        removeImage = true;
        CoverImage.Source = null;
        CoverImage.IsVisible = false;
        RemoveCoverImageButton.IsVisible = false;
        ChooseCoverImageButton.Text = "Choose image";
    }

    private void ShowCoverImage(byte[] bytes)
    {
        CoverImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        CoverImage.IsVisible = true;
        RemoveCoverImageButton.IsVisible = true;
        ChooseCoverImageButton.Text = "Replace image";
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        SaveButton.IsEnabled = false;
        try
        {
            StoredRecipe recipe;
            if (recipeId is not null)
            {
                recipe = await recipeStore.GetByLocalIdAsync(recipeId)
                    ?? throw new InvalidOperationException("The recipe being edited no longer exists.");
            }
            else
            {
                recipe = new StoredRecipe
                {
                    CreatedAtUtc = DateTime.UtcNow,
                    IsProfileData = session.IsSignedIn
                };
            }

            recipe.Title = TitleEntry.Text ?? string.Empty;
            recipe.Description = DescriptionEditor.Text;
            recipe.LanguageCode = RecipeLanguages.All[Math.Max(LanguagePicker.SelectedIndex, 0)].Code;
            recipe.Ingredients = SplitLines(IngredientsEditor.Text);
            recipe.Instructions = SplitLines(InstructionsEditor.Text);
            await recipeStore.SaveLocalAsync(recipe);

            if (pendingImage is { } image)
            {
                await recipeStore.SaveCoverImageAsync(recipe.LocalId, image.Bytes, image.ContentType);
            }
            else if (removeImage)
            {
                await recipeStore.ClearCoverImageAsync(recipe.LocalId);
            }

            await session.SyncAfterLocalChangeAsync();
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception) when (ProfileSession.IsExpectedClientException(exception))
        {
            ShowStatus(await session.DescribeFailureAsync(exception));
        }
        finally
        {
            isBusy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = true;
    }

    private static string[] SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
