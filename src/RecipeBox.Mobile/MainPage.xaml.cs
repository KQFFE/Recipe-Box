using System.Net.Http;
using System.Text.Json;

namespace RecipeBox.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient apiClient;
    private bool isBusy;
    private bool isSignedIn;
    private int currentPage;

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
            await apiClient.CreateRecipeAsync(
                serverAddress,
                new RecipeWriteRequest(
                    TitleEntry.Text ?? string.Empty,
                    DescriptionEntry.Text,
                    languageCode,
                    SplitLines(IngredientsEditor.Text),
                    SplitLines(InstructionsEditor.Text)));

            TitleEntry.Text = string.Empty;
            DescriptionEntry.Text = string.Empty;
            IngredientsEditor.Text = string.Empty;
            InstructionsEditor.Text = string.Empty;
            StatusLabel.Text = "Recipe saved.";
            await LoadRecipesAsync();
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

        return new Border
        {
            Padding = 14,
            Stroke = (Color)Application.Current!.Resources["Gray200"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = content
        };
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
        CreateButton.IsEnabled = enabled;
    }

    private static string[] SplitLines(string? value) =>
        (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsExpectedClientException(Exception exception) =>
        exception is MobileApiException
            or HttpRequestException
            or InvalidOperationException
            or JsonException
            or OperationCanceledException;
}
