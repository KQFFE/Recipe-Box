using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

public sealed class ImportModel(
    ApplicationDbContext db,
    IRecipeUrlImporter recipeUrlImporter)
    : RecipePageModel
{
    [BindProperty]
    [Display(Name = "Recipe page URL")]
    public string? RecipeUrl { get; set; }

    [BindProperty]
    public RecipeFormModel Input { get; set; } = new();

    public bool IsReview { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
    {
        if (!RecipeUrlImporter.TryParseRecipeUri(RecipeUrl, out var recipeUri) || recipeUri is null)
        {
            ModelState.AddModelError(nameof(RecipeUrl), "Enter a valid public HTTP or HTTPS recipe page URL.");
            return Page();
        }

        try
        {
            RecipeUrlImporter.ValidateRecipeUri(recipeUri);
            var draft = await recipeUrlImporter.ImportAsync(recipeUri, cancellationToken);
            ModelState.Clear();
            Input = new RecipeFormModel
            {
                Title = draft.Title,
                LanguageCode = RecipeLanguage.ResolveCode(
                    draft.LanguageCode,
                    string.Join(
                        Environment.NewLine,
                        draft.Title,
                        draft.Description,
                        draft.Ingredients,
                        draft.Instructions)),
                Description = draft.Description,
                ImageUrl = draft.ImageUrl,
                Ingredients = draft.Ingredients,
                Instructions = draft.Instructions
            };
            IsReview = true;
            return Page();
        }
        catch (RecipeImportException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(string.Empty, "The recipe website could not be reached. Check the URL and try again.");
        }
        catch (IOException)
        {
            ModelState.AddModelError(string.Empty, "The recipe page could not be read. Check the URL and try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ModelState.AddModelError(string.Empty, "The recipe website took too long to respond. Try again later.");
        }

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
