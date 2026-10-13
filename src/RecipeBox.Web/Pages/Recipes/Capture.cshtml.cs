using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

[Authorize]
[RequestSizeLimit(11 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
public sealed class CaptureModel(
    ApplicationDbContext db,
    IRecipeTextExtractor textExtractor)
    : RecipePageModel
{
    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public bool IsReview { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostExtractAsync(
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (photo is null || !RecipeImageUpload.IsWithinSizeLimit(photo.Length))
        {
            ModelState.AddModelError(string.Empty, "Choose an image smaller than 10 MB.");
            return Page();
        }

        await using var buffer = new MemoryStream((int)photo.Length);
        await photo.CopyToAsync(buffer, cancellationToken);
        var imageBytes = buffer.ToArray();

        if (!RecipeImageUpload.HasSupportedSignature(imageBytes))
        {
            ModelState.AddModelError(string.Empty, "Upload a valid JPEG, PNG, or WebP image.");
            return Page();
        }

        ModelState.Clear();
        var draft = RecipeDraftParser.Parse(textExtractor.ExtractText(imageBytes));
        if (string.IsNullOrWhiteSpace(draft.Title)
            && string.IsNullOrWhiteSpace(draft.Ingredients)
            && string.IsNullOrWhiteSpace(draft.Instructions))
        {
            ModelState.AddModelError(string.Empty, "No readable text was found. Try a clearer photo.");
            return Page();
        }

        Input = new RecipeFormModel
        {
            Title = draft.Title,
            LanguageCode = RecipeLanguage.ResolveCode(
                draft.LanguageCode,
                string.Join(Environment.NewLine, draft.Title, draft.Ingredients, draft.Instructions)),
            Ingredients = draft.Ingredients,
            Instructions = draft.Instructions
        };
        IsReview = true;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        IsReview = true;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            Title = Input.Title.Trim(),
            Description = Input.Description?.Trim(),
            ImageUrl = Input.ImageUrl?.Trim(),
            Ingredients = RecipeContent.NormalizeLines(Input.Ingredients ?? string.Empty),
            Instructions = RecipeContent.NormalizeLines(Input.Instructions ?? string.Empty),
            LanguageCode = RecipeLanguage.NormalizeCode(Input.LanguageCode),
            OwnerId = CurrentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("Details", new { id = recipe.Id });
    }
}
