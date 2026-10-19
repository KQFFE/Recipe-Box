using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public partial class ImportPage : ContentPage
{
    private readonly RecipePageImporter importer;
    private readonly RecipeDraftState draftState;
    private bool isBusy;

    public ImportPage(RecipePageImporter importer, RecipeDraftState draftState)
    {
        InitializeComponent();
        this.importer = importer;
        this.draftState = draftState;
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        ImportButton.IsEnabled = false;
        StatusBorder.IsVisible = false;
        try
        {
            var recipe = await importer.ImportAsync(UrlEntry.Text ?? string.Empty);
            draftState.Pending = new RecipeDraftResponse(
                recipe.Title,
                recipe.Description,
                recipe.ImageUrl,
                recipe.LanguageCode,
                recipe.IngredientLines,
                recipe.InstructionLines);
            UrlEntry.Text = string.Empty;
            await Shell.Current.GoToAsync(nameof(RecipeFormPage));
        }
        catch (RecipeImportFailedException exception)
        {
            ShowStatus(exception.Message);
        }
        finally
        {
            isBusy = false;
            ImportButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusBorder.IsVisible = true;
    }
}
