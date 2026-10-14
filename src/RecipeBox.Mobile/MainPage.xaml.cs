using System.Net.Http;
using System.Text.Json;

namespace RecipeBox.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient apiClient;
    private bool isBusy;
    private bool isSignedIn;
    private int currentPage;
    private int? editingRecipeId;

    public MainPage(MobileApiClient apiClient)
    {
        InitializeComponent();
        this.apiClient = apiClient;
        ServerAddressEntry.Text = Preferences.Default.Get(MobileApiClient.ServerAddressPreferenceKey, string.Empty);
        LanguagePicker.SelectedIndex = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        try
        {
            if (await apiClient.HasStoredSessionAsync(serverAddress))
            {
                SetSignedIn(true);
                await LoadRecipesAsync();
            }
        }
        catch (Exception exception) when (IsExpectedClientException(exception))
        {
            if (exception is MobileApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            {
                SetSignedIn(false);
            }

            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            Preferences.Default.Set(MobileApiClient.ServerAddressPreferenceKey, serverAddress.AbsoluteUri);
            StatusLabel.Text = "Opening secure sign-in in your browser...";
            await apiClient.SignInAsync(serverAddress);
            SetSignedIn(true);
            await LoadRecipesAsync();
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(async () =>
        {
            try
            {
                await apiClient.SignOutAsync(
                    new Uri(Preferences.Default.Get(MobileApiClient.ServerAddressPreferenceKey, string.Empty)));
                StatusLabel.Text = "You have been signed out.";
            }
            finally
            {
                RecipeList.Children.Clear();
                SetSignedIn(false);
            }
        });
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(() => LoadRecipesAsync());
    }

    private async void OnLoadMoreClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(() => LoadRecipesAsync(loadNextPage: true));
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            var languageCode = LanguagePicker.SelectedIndex == 1 ? "sv" : "en";
            var recipe = new RecipeWriteRequest(
                TitleEntry.Text ?? string.Empty,
                DescriptionEntry.Text,
                languageCode,
                SplitLines(IngredientsEditor.Text),
                SplitLines(InstructionsEditor.Text));
            if (editingRecipeId is int recipeId)
            {
                await apiClient.UpdateRecipeAsync(serverAddress, recipeId, recipe);
                ResetRecipeForm();
                await LoadRecipesAsync();
                StatusLabel.Text = "Recipe updated.";
                return;
            }

            await apiClient.CreateRecipeAsync(serverAddress, recipe);
            ResetRecipeForm();
            await LoadRecipesAsync();
            StatusLabel.Text = "Recipe saved.";
        });
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ResetRecipeForm();
        StatusLabel.Text = "Editing cancelled.";
    }

    private async void OnCapturePhotoClicked(object? sender, EventArgs e)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Capture a recipe"
            });
            if (photo is null)
            {
                StatusLabel.Text = "Photo capture was cancelled.";
                return;
            }

            await using var photoStream = await photo.OpenReadAsync();
            var (bytes, contentType, fileName) = await ReadPhotoAsync(photoStream);
            var draft = await apiClient.ExtractPhotoDraftAsync(
                serverAddress,
                bytes,
                contentType,
                fileName);
            ShowDraftForReview(draft);
        });
    }

    private async void OnImportUrlClicked(object? sender, EventArgs e)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            var draft = await apiClient.ImportRecipeDraftAsync(
                serverAddress,
                ImportUrlEntry.Text ?? string.Empty);
            ShowDraftForReview(draft);
        });
    }

    private async void OnChooseCoverImageClicked(object? sender, EventArgs e)
    {
        if (editingRecipeId is not int recipeId
            || !TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
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
            var (bytes, contentType, fileName) = await ReadPhotoAsync(photoStream);
            await apiClient.UploadRecipeImageAsync(
                serverAddress,
                recipeId,
                bytes,
                contentType,
                fileName);
            await LoadEditingCoverImageAsync(serverAddress, recipeId);
            StatusLabel.Text = "Cover image updated.";
        });
    }

    private async void OnRemoveCoverImageClicked(object? sender, EventArgs e)
    {
        if (editingRecipeId is not int recipeId
            || !await DisplayAlertAsync(
                "Remove cover image?",
                "This removes the cover image from this recipe.",
                "Remove",
                "Cancel")
            || !TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            await apiClient.DeleteRecipeImageAsync(serverAddress, recipeId);
            CoverImage.Source = null;
            CoverImage.IsVisible = false;
            RemoveCoverImageButton.IsVisible = false;
            StatusLabel.Text = "Cover image removed.";
        });
    }

    private async Task LoadRecipesAsync(bool loadNextPage = false)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        var requestedPage = loadNextPage ? currentPage + 1 : 1;
        var recipeList = await apiClient.GetRecipesAsync(serverAddress, requestedPage);
        if (!loadNextPage)
        {
            RecipeList.Children.Clear();
        }

        if (recipeList.Items.Count == 0 && !loadNextPage)
        {
            RecipeList.Children.Add(new Label { Text = "No recipes yet. Add one below." });
        }
        else
        {
            foreach (var recipe in recipeList.Items)
            {
                RecipeList.Children.Add(CreateRecipeCard(recipe));
            }
        }

        currentPage = recipeList.Page;
        LoadMoreButton.IsVisible = currentPage * recipeList.PageSize < recipeList.TotalCount;
        StatusLabel.Text =
            $"Loaded {Math.Min(currentPage * recipeList.PageSize, recipeList.TotalCount)} of {recipeList.TotalCount} recipe(s).";
    }

    private View CreateRecipeCard(RecipeResponse recipe)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        content.Children.Add(new Label
        {
            Text = recipe.Title,
            FontAttributes = FontAttributes.Bold,
            FontSize = 18
        });
        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            content.Children.Add(new Label
            {
                Text = recipe.Description,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        content.Children.Add(new Label
        {
            Text = $"{recipe.Ingredients.Count} ingredients | {recipe.Instructions.Count} steps",
            TextColor = Colors.Gray
        });

        var details = new VerticalStackLayout
        {
            IsVisible = false,
            Spacing = 6
        };
        details.Children.Add(new Label
        {
            Text = "Ingredients",
            FontAttributes = FontAttributes.Bold
        });
        details.Children.Add(new Label
        {
            Text = string.Join(Environment.NewLine, recipe.Ingredients.Select(ingredient => $"- {ingredient}")),
            LineBreakMode = LineBreakMode.WordWrap
        });
        details.Children.Add(new Label
        {
            Text = "Instructions",
            FontAttributes = FontAttributes.Bold
        });
        details.Children.Add(new Label
        {
            Text = string.Join(
                Environment.NewLine,
                recipe.Instructions.Select((instruction, index) => $"{index + 1}. {instruction}")),
            LineBreakMode = LineBreakMode.WordWrap
        });
        var toggleDetailsButton = new Button
        {
            Text = "View recipe"
        };
        toggleDetailsButton.Command = new Command(() =>
            {
                details.IsVisible = !details.IsVisible;
                toggleDetailsButton.Text = details.IsVisible ? "Hide recipe" : "View recipe";
            });
        content.Children.Add(toggleDetailsButton);
        content.Children.Add(details);

        var recipeActions = new HorizontalStackLayout { Spacing = 8 };
        var editButton = new Button { Text = "Edit" };
        editButton.Clicked += async (_, _) =>
        {
            await RunBusyOperationAsync(() => BeginEditingRecipeAsync(recipe));
        };
        var deleteButton = new Button { Text = "Delete" };
        deleteButton.Clicked += async (_, _) => await DeleteRecipeAsync(recipe);
        recipeActions.Children.Add(editButton);
        recipeActions.Children.Add(deleteButton);
        content.Children.Add(recipeActions);

        return new Border
        {
            Padding = 14,
            Stroke = (Color)Application.Current!.Resources["Gray200"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = content
        };
    }

    private async Task BeginEditingRecipeAsync(RecipeResponse recipe)
    {
        if (!TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        editingRecipeId = recipe.Id;
        RecipeFormHeading.Text = "Edit recipe";
        CreateButton.Text = "Save changes";
        CancelEditButton.IsVisible = true;
        CapturePhotoButton.IsVisible = false;
        ImportUrlEntry.IsVisible = false;
        ImportUrlButton.IsVisible = false;
        ChooseCoverImageButton.IsVisible = true;
        TitleEntry.Text = recipe.Title;
        DescriptionEntry.Text = recipe.Description;
        LanguagePicker.SelectedIndex = recipe.LanguageCode == "sv" ? 1 : 0;
        IngredientsEditor.Text = string.Join(Environment.NewLine, recipe.Ingredients);
        InstructionsEditor.Text = string.Join(Environment.NewLine, recipe.Instructions);
        await LoadEditingCoverImageAsync(serverAddress, recipe.Id);
        StatusLabel.Text = $"Editing {recipe.Title}.";
        await PageScrollView.ScrollToAsync(TitleEntry, ScrollToPosition.Start, animated: true);
    }

    private async Task LoadEditingCoverImageAsync(Uri serverAddress, int recipeId)
    {
        var imageBytes = await apiClient.GetRecipeImageAsync(serverAddress, recipeId);
        CoverImage.Source = imageBytes is null
            ? null
            : ImageSource.FromStream(() => new MemoryStream(imageBytes));
        CoverImage.IsVisible = imageBytes is not null;
        RemoveCoverImageButton.IsVisible = imageBytes is not null;
    }

    private async Task DeleteRecipeAsync(RecipeResponse recipe)
    {
        if (isBusy
            || !await DisplayAlertAsync(
                "Delete recipe?",
                $"Delete \"{recipe.Title}\" and its cover image? This cannot be undone.",
                "Delete",
                "Cancel")
            || !TryGetServerAddress(out var serverAddress))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            await apiClient.DeleteRecipeAsync(serverAddress, recipe.Id);
            if (editingRecipeId == recipe.Id)
            {
                ResetRecipeForm();
            }

            await LoadRecipesAsync();
            StatusLabel.Text = "Recipe deleted.";
        });
    }

    private void ResetRecipeForm()
    {
        editingRecipeId = null;
        RecipeFormHeading.Text = "Add a recipe";
        CreateButton.Text = "Save recipe";
        CancelEditButton.IsVisible = false;
        CapturePhotoButton.IsVisible = true;
        ImportUrlEntry.IsVisible = true;
        ImportUrlButton.IsVisible = true;
        ChooseCoverImageButton.IsVisible = false;
        RemoveCoverImageButton.IsVisible = false;
        CoverImage.Source = null;
        CoverImage.IsVisible = false;
        TitleEntry.Text = string.Empty;
        DescriptionEntry.Text = string.Empty;
        LanguagePicker.SelectedIndex = 0;
        IngredientsEditor.Text = string.Empty;
        InstructionsEditor.Text = string.Empty;
    }

    private void ShowDraftForReview(RecipeDraftResponse draft)
    {
        TitleEntry.Text = draft.Title;
        DescriptionEntry.Text = draft.Description;
        LanguagePicker.SelectedIndex = draft.LanguageCode == "sv" ? 1 : 0;
        IngredientsEditor.Text = string.Join(Environment.NewLine, draft.Ingredients);
        InstructionsEditor.Text = string.Join(Environment.NewLine, draft.Instructions);
        StatusLabel.Text = "Review and edit the extracted recipe, then save it.";
        TitleEntry.Focus();
    }

    private static async Task<(byte[] Bytes, string ContentType, string FileName)> ReadPhotoAsync(
        Stream photoStream)
    {
        const int maximumBytes = 10 * 1024 * 1024;
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var bytesRead = await photoStream.ReadAsync(chunk);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > maximumBytes)
            {
                throw new InvalidDataException("Choose a photo smaller than 10 MB.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead));
        }

        var bytes = buffer.ToArray();
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return (bytes, "image/jpeg", "capture.jpg");
        }

        if (bytes.Length >= 8
            && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (bytes, "image/png", "capture.png");
        }

        if (bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return (bytes, "image/webp", "capture.webp");
        }

        throw new InvalidDataException("The captured photo must be JPEG, PNG, or WebP.");
    }

    private async Task RunBusyOperationAsync(Func<Task> operation)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        SetControlsEnabled(false);
        try
        {
            await operation();
        }
        catch (Exception exception) when (IsExpectedClientException(exception))
        {
            if (exception is MobileApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            {
                SetSignedIn(false);
            }

            StatusLabel.Text = exception.Message;
        }
        finally
        {
            isBusy = false;
            SetControlsEnabled(true);
        }
    }

    private bool TryGetServerAddress(out Uri serverAddress)
    {
        if (Uri.TryCreate(ServerAddressEntry.Text?.Trim(), UriKind.Absolute, out var parsedAddress)
            && parsedAddress.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(parsedAddress.UserInfo)
            && string.IsNullOrEmpty(parsedAddress.Query)
            && string.IsNullOrEmpty(parsedAddress.Fragment))
        {
            serverAddress = new Uri(parsedAddress.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
            return true;
        }

        serverAddress = null!;
        StatusLabel.Text = "Enter a valid HTTPS server address, for example https://recipes.example.com.";
        return false;
    }

    private void SetSignedIn(bool signedIn)
    {
        isSignedIn = signedIn;
        SignInButton.IsVisible = !signedIn;
        SignOutButton.IsVisible = signedIn;
        ServerAddressEntry.IsEnabled = !signedIn && !isBusy;
        RecipesSection.IsVisible = signedIn;
    }

    private void SetControlsEnabled(bool enabled)
    {
        SignInButton.IsEnabled = enabled;
        SignOutButton.IsEnabled = enabled;
        ServerAddressEntry.IsEnabled = enabled && !isSignedIn;
        TitleEntry.IsEnabled = enabled;
        DescriptionEntry.IsEnabled = enabled;
        LanguagePicker.IsEnabled = enabled;
        IngredientsEditor.IsEnabled = enabled;
        InstructionsEditor.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        LoadMoreButton.IsEnabled = enabled;
        CapturePhotoButton.IsEnabled = enabled;
        ImportUrlButton.IsEnabled = enabled;
        ImportUrlEntry.IsEnabled = enabled;
        ChooseCoverImageButton.IsEnabled = enabled;
        RemoveCoverImageButton.IsEnabled = enabled;
        CreateButton.IsEnabled = enabled;
        CancelEditButton.IsEnabled = enabled;
    }

    private static string[] SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsExpectedClientException(Exception exception) =>
        exception is MobileApiException
            or HttpRequestException
            or IOException
            or InvalidDataException
            or InvalidOperationException
            or FeatureNotSupportedException
            or PermissionException
            or JsonException
            or OperationCanceledException;
}
