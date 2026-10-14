using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Pages.Recipes;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Api;

public static class RecipesApi
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumRequestBodySize = 64 * 1024;

    public static IEndpointRouteBuilder MapRecipesApi(this IEndpointRouteBuilder endpoints)
    {
        var recipes = endpoints.MapGroup("/api/v1/recipes")
            .WithTags("Recipes")
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBodySize))
            .AddEndpointFilter(async (context, next) =>
            {
                if (context.HttpContext.Request.ContentLength is > MaximumRequestBodySize)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                return await next(context);
            })
            .RequireRateLimiting("recipe-api");

        recipes.MapGet("/", ListAsync)
            .RequireAuthorization("RecipeRead");
        recipes.MapGet("/{id:int}", GetAsync)
            .RequireAuthorization("RecipeRead");
        recipes.MapPost("/", CreateAsync)
            .RequireAuthorization("RecipeWrite");
        recipes.MapPut("/{id:int}", UpdateAsync)
            .RequireAuthorization("RecipeWrite");
        recipes.MapDelete("/{id:int}", DeleteAsync)
            .RequireAuthorization("RecipeWrite");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? DefaultPageSize;
        if (currentPage < 1
            || currentPageSize is < 1 or > MaximumPageSize
            || currentPage > int.MaxValue / currentPageSize)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = [$"Page must be at least 1 and pageSize must be between 1 and {MaximumPageSize}."]
            });
        }

        var ownerId = GetOwnerId(principal);
        var ownedRecipes = db.Recipes.AsNoTracking().Where(recipe => recipe.OwnerId == ownerId);
        var totalCount = await ownedRecipes.CountAsync(cancellationToken);
        var items = await ownedRecipes
            .OrderBy(recipe => recipe.Title)
            .ThenBy(recipe => recipe.Id)
            .Skip((currentPage - 1) * currentPageSize)
            .Take(currentPageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new RecipeListResponse(
            items.Select(RecipeResponse.FromRecipe).ToArray(),
            currentPage,
            currentPageSize,
            totalCount));
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        return recipe is null
            ? Results.NotFound()
            : Results.Ok(RecipeResponse.FromRecipe(recipe));
    }

    private static async Task<IResult> CreateAsync(
        RecipeRequest request,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(request, out var form, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        var now = DateTime.UtcNow;
        var recipe = new Recipe
        {
            Title = form.Title.Trim(),
            Description = form.Description?.Trim(),
            Ingredients = RecipeContent.NormalizeLines(form.Ingredients ?? string.Empty),
            Instructions = RecipeContent.NormalizeLines(form.Instructions ?? string.Empty),
            LanguageCode = RecipeLanguage.NormalizeCode(form.LanguageCode),
            OwnerId = GetOwnerId(principal),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/recipes/{recipe.Id}", RecipeResponse.FromRecipe(recipe));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        RecipeRequest request,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        if (!TryValidate(request, out var form, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        recipe.Title = form.Title.Trim();
        recipe.Description = form.Description?.Trim();
        recipe.Ingredients = RecipeContent.NormalizeLines(form.Ingredients ?? string.Empty);
        recipe.Instructions = RecipeContent.NormalizeLines(form.Instructions ?? string.Empty);
        recipe.LanguageCode = RecipeLanguage.NormalizeCode(form.LanguageCode);
        recipe.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(RecipeResponse.FromRecipe(recipe));
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        IRecipeImageStorage imageStorage,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(recipe.StoredImageKey)
            && !await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                recipe.OwnerId,
                recipe.StoredImageKey,
                loggerFactory.CreateLogger("RecipeBox.Web.Api.RecipesApi"),
                CancellationToken.None))
        {
            httpContext.Response.Headers["X-RecipeBox-Warning"] =
                "The recipe was deleted, but its uploaded image could not be removed.";
        }

        return Results.NoContent();
    }

    private static async Task<Recipe?> FindOwnedRecipeAsync(
        int id,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        CancellationToken cancellationToken) =>
        await db.Recipes.FirstOrDefaultAsync(
            recipe => recipe.Id == id && recipe.OwnerId == GetOwnerId(principal),
            cancellationToken);

    private static string GetOwnerId(ClaimsPrincipal principal) =>
        principal.GetClaim(OpenIddictConstants.Claims.Subject)
        ?? throw new InvalidOperationException("The authenticated API token does not contain a subject.");

    private static bool TryValidate(
        RecipeRequest request,
        out RecipeFormModel form,
        out Dictionary<string, string[]> errors)
    {
        try
        {
            form = request.ToValidatedForm();
            errors = [];
            return true;
        }
        catch (RecipeRequestValidationException exception)
        {
            form = null!;
            errors = exception.ValidationResults
                .SelectMany(result => result.MemberNames.Select(member => new
                {
                    Member = member,
                    Error = result.ErrorMessage ?? "The submitted value is invalid."
                }))
                .GroupBy(error => error.Member)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Error).ToArray());
            if (errors.Count == 0)
            {
                errors["request"] = exception.ValidationResults
                    .Select(result => result.ErrorMessage ?? "The submitted value is invalid.")
                    .ToArray();
            }

            return false;
        }
    }
}
