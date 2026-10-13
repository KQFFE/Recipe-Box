using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Models;

namespace RecipeManager.Web.Pages.Recipes;

[Authorize]
public sealed class LayoutModel(ApplicationDbContext db) : RecipePageModel
{
    [BindProperty]
    public List<string> SectionOrder { get; set; } = [];

    [BindProperty]
    public List<string>? FullWidthSections { get; set; } = [];

    public Recipe Recipe { get; private set; } = null!;

    public IReadOnlyList<RecipeLayoutSection> AvailableSections { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var preference = await db.RecipeLayouts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                layout => layout.RecipeId == id && layout.OwnerId == CurrentUserId,
                cancellationToken);
        var settings = RecipeLayoutDefinition.FromPreference(Recipe, preference);
        SectionOrder = settings.Sections.Select(section => section.Key).ToList();
        FullWidthSections = settings.FullWidthSections.ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostMoveUpAsync(
        int id,
        int index,
        CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!TryGetSettings(out var settings))
        {
            return InvalidLayout();
        }

        var sections = settings.Sections.ToList();
        if (index <= 0 || index >= sections.Count)
        {
            ModelState.AddModelError(string.Empty, "That section cannot be moved up.");
            return Page();
        }

        (sections[index - 1], sections[index]) = (sections[index], sections[index - 1]);
        SectionOrder = sections.Select(section => section.Key).ToList();
        FullWidthSections = settings.FullWidthSections.ToList();
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostMoveDownAsync(
        int id,
        int index,
        CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!TryGetSettings(out var settings))
        {
            return InvalidLayout();
        }

        var sections = settings.Sections.ToList();
        if (index < 0 || index >= sections.Count - 1)
        {
            ModelState.AddModelError(string.Empty, "That section cannot be moved down.");
            return Page();
        }

        (sections[index + 1], sections[index]) = (sections[index], sections[index + 1]);
        SectionOrder = sections.Select(section => section.Key).ToList();
        FullWidthSections = settings.FullWidthSections.ToList();
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadRecipeAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!TryGetSettings(out var settings))
        {
            return InvalidLayout();
        }

        var preference = await db.RecipeLayouts.SingleOrDefaultAsync(
            layout => layout.RecipeId == id && layout.OwnerId == CurrentUserId,
            cancellationToken);
        if (preference is null)
        {
            db.RecipeLayouts.Add(RecipeLayoutDefinition.ToPreference(CurrentUserId, id, settings));
        }
        else
        {
            preference.SectionOrder = string.Join('|', settings.Sections.Select(section => section.Key));
            preference.FullWidthSections = string.Join(
                '|',
                settings.FullWidthSections.Order(StringComparer.Ordinal));
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("Details", new { id });
    }

    private async Task<bool> LoadRecipeAsync(int id, CancellationToken cancellationToken)
    {
        var recipe = await db.Recipes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == id && item.OwnerId == CurrentUserId,
                cancellationToken);
        if (recipe is null)
        {
            return false;
        }

        Recipe = recipe;
        AvailableSections = RecipeLayoutDefinition.AvailableSections(recipe);
        return true;
    }

    private bool TryGetSettings(out RecipeLayoutSettings settings) =>
        RecipeLayoutDefinition.TryCreate(
            AvailableSections,
            SectionOrder,
            FullWidthSections ?? [],
            out settings);

    private IActionResult InvalidLayout()
    {
        var defaults = RecipeLayoutDefinition.Default(Recipe);
        SectionOrder = defaults.Sections.Select(section => section.Key).ToList();
        FullWidthSections = defaults.FullWidthSections.ToList();
        ModelState.Clear();
        ModelState.AddModelError(
            string.Empty,
            "Arrange each available section exactly once and choose valid width settings.");
        return Page();
    }
}
