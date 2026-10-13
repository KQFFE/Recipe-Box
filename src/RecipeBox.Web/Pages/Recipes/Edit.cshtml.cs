using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[Authorize]
[RequestSizeLimit(11 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
public sealed class EditModel(
    ApplicationDbContext db,
    IRecipeImageStorage imageStorage,
    ILogger<EditModel> logger) : RecipePageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        Input = new RecipeFormModel
        {
            Title = recipe.Title,
            Description = recipe.Description,
            ImageUrl = recipe.ImageUrl,
            Ingredients = recipe.Ingredients,
            Instructions = recipe.Instructions
        };
        SetCurrentImage(recipe);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(cancellationToken);
        if (recipe is null)
        {
            return NotFound();
        }

        SetCurrentImage(recipe);
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

        var previousImageKey = recipe.StoredImageKey;
        recipe.Title = Input.Title.Trim();
        recipe.Description = Input.Description?.Trim();
        recipe.ImageUrl = storedImage is not null
            ? null
            : Input.ImageUrl?.Trim();
        if (storedImage is not null)
        {
            recipe.StoredImageKey = storedImage.StorageKey;
            recipe.StoredImageContentType = storedImage.ContentType;
        }
        else if (Input.RemoveUploadedImage)
        {
            recipe.StoredImageKey = null;
            recipe.StoredImageContentType = null;
        }

        recipe.Ingredients = RecipeContent.NormalizeLines(Input.Ingredients ?? string.Empty);
        recipe.Instructions = RecipeContent.NormalizeLines(Input.Instructions ?? string.Empty);
        recipe.UpdatedAtUtc = DateTime.UtcNow;

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

        if (previousImageKey is not null
            && !string.Equals(previousImageKey, recipe.StoredImageKey, StringComparison.Ordinal)
            && !await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                CurrentUserId,
                previousImageKey,
                logger,
                CancellationToken.None))
        {
            TempData["ImageCleanupWarning"] = "The recipe was saved, but an old image file could not be removed.";
        }

        return RedirectToPage("Details", new { id = recipe.Id });
    }

    private void SetCurrentImage(Recipe recipe)
    {
        Input.HasUploadedImage = !string.IsNullOrWhiteSpace(recipe.StoredImageKey);
        Input.CurrentImageSource = Input.HasUploadedImage
            ? Url.Page("/Recipes/Image", values: new { id = recipe.Id })
            : null;
    }

    private Task<Recipe?> FindOwnedRecipeAsync(CancellationToken cancellationToken) =>
        db.Recipes.FirstOrDefaultAsync(
            recipe => recipe.Id == Id && recipe.OwnerId == CurrentUserId,
            cancellationToken);
}
