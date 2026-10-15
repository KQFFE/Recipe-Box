using System.Net.Http;
using System.Text.Json;
using RecipeBox.Mobile.Core;
using SQLite;

namespace RecipeBox.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient apiClient;
    private readonly LocalRecipeStore recipeStore;
    private readonly ProfileSyncService profileSync;
    private bool isBusy;
    private bool isSignedIn;
    private string? editingRecipeId;

    public MainPage(
        MobileApiClient apiClient,
        LocalRecipeStore recipeStore,
        ProfileSyncService profileSync)
    {
        InitializeComponent();
        this.apiClient = apiClient;
        this.recipeStore = recipeStore;
        this.profileSync = profileSync;
        LanguagePicker.SelectedIndex = 0;
        SetSignedIn(false);
#if DEBUG
        ChangeServerButton.IsVisible = true;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await LoadRecipesAsync();
            var serverAddress = GetSavedServerAddress();
            if (serverAddress is not null && await apiClient.HasStoredSessionAsync(serverAddress))
            {
                SetSignedIn(true);
                await profileSync.SyncAsync(serverAddress);
                await LoadRecipesAsync();
                StatusLabel.Text = "Your recipes are synced with your profile.";
            }
        }
        catch (Exception exception) when (IsExpectedClientException(exception))
        {
            if (exception is MobileApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            {
                await apiClient.ClearLocalSessionAsync();
                SetSignedIn(false);
                await recipeStore.RemoveProfileRecipesAsync();
                await LoadRecipesAsync();
            }

            StatusLabel.Text = exception.Message;
        }
    }

    private async void OnSaveToProfileClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(async () =>
        {
            var serverAddress = isSignedIn
                ? GetSavedServerAddress()
                : await GetServerAddressForSignInAsync();
            if (serverAddress is null)
            {
                return;
            }

            Preferences.Default.Set(MobileApiClient.ServerAddressPreferenceKey, serverAddress.AbsoluteUri);
            if (!await apiClient.HasStoredSessionAsync(serverAddress))
            {
                StatusLabel.Text = "Opening secure sign-in in your browser...";
                await apiClient.SignInAsync(serverAddress);
            }

            SetSignedIn(true);
            await profileSync.SyncAsync(serverAddress);
            await LoadRecipesAsync();
            StatusLabel.Text = "Your recipes are synced with your profile.";
        });
    }

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        if (!isSignedIn
            || !await DisplayAlertAsync(
                "Sign out?",
                "Profile recipes and unsynced profile changes will be removed from this device. Device-only recipes will stay.",
                "Sign out",
                "Cancel"))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            try
            {
                var serverAddress = GetSavedServerAddress();
                if (serverAddress is not null)
                {
                    await apiClient.SignOutAsync(serverAddress);
                }
            }
            finally
            {
                await recipeStore.RemoveProfileRecipesAsync();
                SetSignedIn(false);
                await LoadRecipesAsync();
                StatusLabel.Text = "You have been signed out. Device-only recipes remain on this device.";
            }
        });
    }

    private async void OnChangeServerClicked(object? sender, EventArgs e)
    {
#if DEBUG
        if (isBusy)
        {
            return;
        }

        var serverAddress = await GetServerAddressForSignInAsync();
        if (serverAddress is null)
        {
            return;
        }

        var previousAddress = GetSavedServerAddress();
        if (previousAddress is not null
            && string.Equals(previousAddress.AbsoluteUri, serverAddress.AbsoluteUri, StringComparison.Ordinal))
        {
            return;
        }

        if (!await DisplayAlertAsync(
                "Change development server?",
                "This removes profile recipes from this device. They remain on the server and can be downloaded again.",
                "Change server",
                "Cancel"))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            await apiClient.ClearLocalSessionAsync();
            await recipeStore.RemoveProfileRecipesAsync();
            Preferences.Default.Set(MobileApiClient.ServerAddressPreferenceKey, serverAddress.AbsoluteUri);
            SetSignedIn(false);
            await LoadRecipesAsync();
            StatusLabel.Text = "Development server changed. Save to Profile to sign in again.";
        });
#endif
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(async () =>
        {
            if (isSignedIn && GetSavedServerAddress() is Uri serverAddress)
            {
                await profileSync.SyncAsync(serverAddress);
            }

            await LoadRecipesAsync();
        });
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        await RunBusyOperationAsync(async () =>
        {
            var wasEditing = editingRecipeId is not null;
            StoredRecipe recipe;
            if (editingRecipeId is string recipeId)
            {
                recipe = await recipeStore.GetByLocalIdAsync(recipeId)
                    ?? throw new InvalidOperationException("The recipe being edited no longer exists.");
            }
            else
            {
                recipe = new StoredRecipe
                {
                    CreatedAtUtc = DateTime.UtcNow,
                    IsProfileData = isSignedIn
                };
            }

            recipe.Title = TitleEntry.Text ?? string.Empty;
            recipe.Description = DescriptionEntry.Text;
            recipe.LanguageCode = LanguagePicker.SelectedIndex == 1 ? "sv" : "en";
            recipe.Ingredients = SplitLines(IngredientsEditor.Text);
            recipe.Instructions = SplitLines(InstructionsEditor.Text);
            await recipeStore.SaveLocalAsync(recipe);

            ResetRecipeForm();
            await LoadRecipesAsync();
            StatusLabel.Text = wasEditing ? "Recipe updated on this device." : "Recipe saved on this device.";
            await SyncAfterLocalChangeAsync();
        });
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        ResetRecipeForm();
        StatusLabel.Text = "Editing cancelled.";
    }

    private async void OnCapturePhotoClicked(object? sender, EventArgs e)
    {
        if (!TryGetSignedInServerAddress(out var serverAddress))
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
        if (!TryGetSignedInServerAddress(out var serverAddress))
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
        if (editingRecipeId is not string recipeId)
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
            await recipeStore.SaveCoverImageAsync(recipeId, bytes, contentType);
            await LoadEditingCoverImageAsync(recipeId);
            StatusLabel.Text = "Cover image saved on this device.";
            await SyncAfterLocalChangeAsync();
        });
    }

    private async void OnRemoveCoverImageClicked(object? sender, EventArgs e)
    {
        if (editingRecipeId is not string recipeId
            || !await DisplayAlertAsync(
                "Remove cover image?",
                "This removes the cover image from this recipe.",
                "Remove",
                "Cancel"))
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            await recipeStore.ClearCoverImageAsync(recipeId);
            CoverImage.Source = null;
            CoverImage.IsVisible = false;
            RemoveCoverImageButton.IsVisible = false;
            StatusLabel.Text = "Cover image removed from this device.";
            await SyncAfterLocalChangeAsync();
        });
    }

    private async Task LoadRecipesAsync()
    {
        var recipes = await recipeStore.GetRecipesAsync();
        RecipeList.Children.Clear();

        if (recipes.Count == 0)
        {
            RecipeList.Children.Add(new Label { Text = "No recipes yet. Add one below." });
        }
        else
        {
            foreach (var recipe in recipes)
            {
                RecipeList.Children.Add(CreateRecipeCard(recipe));
            }
        }

        StatusLabel.Text = $"{recipes.Count} recipe(s) on this device.";
    }

    private View CreateRecipeCard(StoredRecipe recipe)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        if (recipe.CoverImagePath is string imagePath && File.Exists(imagePath))
        {
            content.Children.Add(new Image
            {
                Source = ImageSource.FromFile(imagePath),
                HeightRequest = 150,
                Aspect = Aspect.AspectFill
            });
        }

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

    private async Task BeginEditingRecipeAsync(StoredRecipe recipe)
    {
        editingRecipeId = recipe.LocalId;
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
        await LoadEditingCoverImageAsync(recipe.LocalId);
        StatusLabel.Text = $"Editing {recipe.Title}.";
        await PageScrollView.ScrollToAsync(TitleEntry, ScrollToPosition.Start, animated: true);
    }

    private async Task LoadEditingCoverImageAsync(string localId)
    {
        var recipe = await recipeStore.GetByLocalIdAsync(localId)
            ?? throw new InvalidOperationException("The recipe being edited no longer exists.");
        var imageBytes = await recipeStore.ReadCoverImageAsync(recipe);
        CoverImage.Source = imageBytes is null
            ? null
            : ImageSource.FromStream(() => new MemoryStream(imageBytes));
        CoverImage.IsVisible = imageBytes is not null;
        RemoveCoverImageButton.IsVisible = imageBytes is not null;
    }

    private async Task DeleteRecipeAsync(StoredRecipe recipe)
    {
        if (isBusy
            || !await DisplayAlertAsync(
                "Delete recipe?",
                $"Delete \"{recipe.Title}\" and its cover image? This cannot be undone.",
                "Delete",
                "Cancel")
            )
        {
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            await recipeStore.DeleteAsync(recipe.LocalId);
            if (editingRecipeId == recipe.LocalId)
            {
                ResetRecipeForm();
            }

            await LoadRecipesAsync();
            StatusLabel.Text = "Recipe deleted on this device.";
            await SyncAfterLocalChangeAsync();
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
                await apiClient.ClearLocalSessionAsync();
                SetSignedIn(false);
                await recipeStore.RemoveProfileRecipesAsync();
                await LoadRecipesAsync();
            }

            StatusLabel.Text = exception.Message;
        }
        finally
        {
            isBusy = false;
            SetControlsEnabled(true);
        }
    }

    private async Task<Uri?> GetServerAddressForSignInAsync()
    {
#if DEBUG
        var configuredAddress = MobileApiClient.GetConfiguredServerAddress();
        var savedAddress = Preferences.Default.Get(MobileApiClient.ServerAddressPreferenceKey, string.Empty);
        var initialAddress = string.IsNullOrWhiteSpace(savedAddress)
            ? configuredAddress?.AbsoluteUri ?? string.Empty
            : savedAddress;
        var enteredAddress = await DisplayPromptAsync(
            "Save to Profile",
            "Enter your Recipe Box server's HTTPS address. The development tunnel URL changes when restarted.",
            initialValue: initialAddress,
            keyboard: Keyboard.Url);
        if (enteredAddress is null)
        {
            return null;
        }

        if (TryNormalizeServerAddress(enteredAddress, out var enteredServerAddress))
        {
            return enteredServerAddress;
        }

        StatusLabel.Text = "Enter a valid HTTPS server address.";
        return null;
#else
        var configuredAddress = MobileApiClient.GetConfiguredServerAddress();
        if (configuredAddress is null)
        {
            StatusLabel.Text = "Profile sync is not configured for this app build yet.";
        }

        return configuredAddress;
#endif
    }

    private Uri? GetSavedServerAddress()
    {
#if !DEBUG
        return MobileApiClient.GetConfiguredServerAddress();
#else
        var savedAddress = Preferences.Default.Get(MobileApiClient.ServerAddressPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedAddress))
        {
            return MobileApiClient.GetConfiguredServerAddress();
        }

        if (TryNormalizeServerAddress(savedAddress, out var serverAddress))
        {
            return serverAddress;
        }

        StatusLabel.Text = "The saved profile server address is invalid. Sign out and save to Profile again.";
        return null;
#endif
    }

    private static bool TryNormalizeServerAddress(string value, out Uri serverAddress)
    {
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsedAddress)
            && parsedAddress.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(parsedAddress.Host)
            && string.IsNullOrEmpty(parsedAddress.UserInfo)
            && string.IsNullOrEmpty(parsedAddress.Query)
            && string.IsNullOrEmpty(parsedAddress.Fragment))
        {
            serverAddress = new Uri(parsedAddress.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
            return true;
        }

        serverAddress = null!;
        return false;
    }

    private bool TryGetSignedInServerAddress(out Uri serverAddress)
    {
        if (isSignedIn && GetSavedServerAddress() is Uri savedAddress)
        {
            serverAddress = savedAddress;
            return true;
        }

        serverAddress = null!;
        StatusLabel.Text = "Save to Profile before using photo capture or URL import.";
        return false;
    }

    private async Task SyncAfterLocalChangeAsync()
    {
        if (!isSignedIn || GetSavedServerAddress() is not Uri serverAddress)
        {
            return;
        }

        try
        {
            await profileSync.SyncAsync(serverAddress);
            await LoadRecipesAsync();
            StatusLabel.Text = "Saved on this device and synced with your profile.";
        }
        catch (Exception exception) when (IsExpectedClientException(exception))
        {
            StatusLabel.Text = $"Saved on this device. Profile sync failed: {exception.Message}";
        }
    }

    private void SetSignedIn(bool signedIn)
    {
        isSignedIn = signedIn;
        SaveToProfileButton.Text = signedIn ? "Sync Profile" : "Save to Profile";
        SignOutButton.IsVisible = signedIn;
        CapturePhotoButton.IsEnabled = signedIn && !isBusy;
        ImportUrlButton.IsEnabled = signedIn && !isBusy;
        ImportUrlEntry.IsEnabled = signedIn && !isBusy;
    }

    private void SetControlsEnabled(bool enabled)
    {
        SaveToProfileButton.IsEnabled = enabled;
        SignOutButton.IsEnabled = enabled;
        ChangeServerButton.IsEnabled = enabled;
        TitleEntry.IsEnabled = enabled;
        DescriptionEntry.IsEnabled = enabled;
        LanguagePicker.IsEnabled = enabled;
        IngredientsEditor.IsEnabled = enabled;
        InstructionsEditor.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
        CapturePhotoButton.IsEnabled = enabled && isSignedIn;
        ImportUrlButton.IsEnabled = enabled && isSignedIn;
        ImportUrlEntry.IsEnabled = enabled && isSignedIn;
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
            or SQLiteException
            or OperationCanceledException;
}
