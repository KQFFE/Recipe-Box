using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[Authorize]
[RequestSizeLimit(11 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
public sealed class CreateModel(
    ApplicationDbContext db,
    IRecipeImageStorage imageStorage,
    ILogger<CreateModel> logger) : RecipePageModel
{
    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        byte[]? imageBytes;
        try
        {
            imageBytes = await RecipeImageUpload.ReadAsync(Input.ImageUpload, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            ModelState.AddModelError("Input.ImageUpload", exception.Message);
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        StoredRecipeImage? storedImage = null;
        if (imageBytes is not null)
        {
            storedImage = await imageStorage.SaveAsync(CurrentUserId, imageBytes, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            Title = Input.Title.Trim(),
            Description = Input.Description?.Trim(),
            ImageUrl = storedImage is null ? Input.ImageUrl?.Trim() : null,
            StoredImageKey = storedImage?.StorageKey,
            StoredImageContentType = storedImage?.ContentType,
            Ingredients = RecipeContent.NormalizeLines(Input.Ingredients ?? string.Empty),
            Instructions = RecipeContent.NormalizeLines(Input.Instructions ?? string.Empty),
            LanguageCode = RecipeLanguage.NormalizeCode(Input.LanguageCode),
            OwnerId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Recipes.Add(recipe);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (storedImage is not null)
            {
                await RecipeImageStorageCleanup.TryDeleteAsync(
                    imageStorage,
                    CurrentUserId,
                    storedImage.StorageKey,
                    logger,
                    CancellationToken.None);
            }

            throw;
        }

        return RedirectToPage("Details", new { id = recipe.Id });
    }
}
