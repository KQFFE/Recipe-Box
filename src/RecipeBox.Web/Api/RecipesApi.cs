using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Pages.Recipes;
using RecipeBox.Web.Services;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace RecipeBox.Web.Api;

public static class RecipesApi
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumRequestBodySize = 64 * 1024;
    private const int MaximumPhotoRequestBodySize = 11 * 1024 * 1024;

    public static IEndpointRouteBuilder MapRecipesApi(this IEndpointRouteBuilder endpoints)
    {
        var recipes = endpoints.MapGroup("/api/v1/recipes")
            .WithTags("Recipes")
            .RequireRateLimiting("recipe-api");

        LimitRequestSize(recipes.MapGet("/", ListAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeRead");
        LimitRequestSize(recipes.MapGet("/{id:int}", GetAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeRead");
        LimitRequestSize(recipes.MapGet("/{id:int}/image", GetImageAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeRead");
        LimitRequestSize(recipes.MapPost("/", CreateAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(recipes.MapPut("/{id:int}", UpdateAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(recipes.MapDelete("/{id:int}", DeleteAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(
            recipes.MapPost("/{id:int}/image", UploadImageAsync),
            MaximumPhotoRequestBodySize,
            multipartBodyLengthLimit: MaximumPhotoRequestBodySize)
            .DisableAntiforgery()
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(recipes.MapDelete("/{id:int}/image", DeleteImageAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(
            recipes.MapPost("/drafts/photo", ExtractPhotoDraftAsync),
            MaximumPhotoRequestBodySize,
            multipartBodyLengthLimit: MaximumPhotoRequestBodySize)
            .DisableAntiforgery()
            .RequireAuthorization("RecipeWrite");
        LimitRequestSize(recipes.MapPost("/drafts/import", ImportRecipeDraftAsync), MaximumRequestBodySize)
            .RequireAuthorization("RecipeWrite");

        return endpoints;
    }

    private static RouteHandlerBuilder LimitRequestSize(
        RouteHandlerBuilder endpoint,
        int maximumRequestBodySize,
        long? multipartBodyLengthLimit = null) =>
        endpoint
            .WithMetadata(
                new RequestSizeLimitAttribute(maximumRequestBodySize),
                new RequestFormLimitsAttribute
                {
                    MultipartBodyLengthLimit = multipartBodyLengthLimit ?? maximumRequestBodySize
                })
            .AddEndpointFilter(async (context, next) =>
            {
                if (context.HttpContext.Request.ContentLength is long contentLength
                    && contentLength > maximumRequestBodySize)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                return await next(context);
            });

    private static async Task<IResult> GetImageAsync(
        int id,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        IRecipeImageStorage imageStorage,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        if (recipe?.StoredImageKey is null
            || recipe.StoredImageContentType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        var stream = await imageStorage.OpenReadAsync(
            recipe.OwnerId,
            recipe.StoredImageKey,
            cancellationToken);
        return stream is null
            ? Results.NotFound()
            : Results.File(stream, recipe.StoredImageContentType);
    }

    private static async Task<IResult> UploadImageAsync(
        int id,
        IFormFile? image,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        IRecipeImageStorage imageStorage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        if (recipe is null)
        {
            return Results.NotFound();
        }

        byte[]? imageBytes;
        try
        {
            imageBytes = await RecipeImageUpload.ReadAsync(image, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["image"] = [exception.Message]
            });
        }

        if (imageBytes is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["image"] = ["Choose an image to upload."]
            });
        }

        var previousImageKey = recipe.StoredImageKey;
        var storedImage = await imageStorage.SaveAsync(recipe.OwnerId, imageBytes, cancellationToken);
        recipe.StoredImageKey = storedImage.StorageKey;
        recipe.StoredImageContentType = storedImage.ContentType;
        recipe.ImageUrl = null;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                recipe.OwnerId,
                storedImage.StorageKey,
                loggerFactory.CreateLogger("RecipeBox.Web.Api.RecipesApi"),
                CancellationToken.None);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(previousImageKey)
            && !await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                recipe.OwnerId,
                previousImageKey,
                loggerFactory.CreateLogger("RecipeBox.Web.Api.RecipesApi"),
                CancellationToken.None))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The image was replaced, but the previous image could not be removed.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteImageAsync(
        int id,
        ClaimsPrincipal principal,
        ApplicationDbContext db,
        IRecipeImageStorage imageStorage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var recipe = await FindOwnedRecipeAsync(id, principal, db, cancellationToken);
        if (recipe?.StoredImageKey is null)
        {
            return Results.NotFound();
        }

        var previousImageKey = recipe.StoredImageKey;
        recipe.StoredImageKey = null;
        recipe.StoredImageContentType = null;
        await db.SaveChangesAsync(cancellationToken);
        if (!await RecipeImageStorageCleanup.TryDeleteAsync(
                imageStorage,
                recipe.OwnerId,
                previousImageKey,
                loggerFactory.CreateLogger("RecipeBox.Web.Api.RecipesApi"),
                CancellationToken.None))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The image was detached from the recipe, but could not be removed from storage.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ExtractPhotoDraftAsync(
        IFormFile? photo,
        IRecipeTextExtractor textExtractor,
        CancellationToken cancellationToken)
    {
        byte[]? imageBytes;
        try
        {
            imageBytes = await RecipeImageUpload.ReadAsync(photo, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["photo"] = [exception.Message]
            });
        }

        if (imageBytes is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["photo"] = ["Choose a photo to scan."]
            });
        }

        var draft = RecipeDraftParser.Parse(textExtractor.ExtractText(imageBytes));
        if (string.IsNullOrWhiteSpace(draft.Title)
            && string.IsNullOrWhiteSpace(draft.Ingredients)
            && string.IsNullOrWhiteSpace(draft.Instructions))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "No readable recipe text was found.",
                detail: "Try taking a clearer photo.");
        }

        return Results.Ok(RecipeDraftResponse.FromDraft(draft));
    }

    private static async Task<IResult> ImportRecipeDraftAsync(
        RecipeDraftRequest request,
        IRecipeUrlImporter recipeUrlImporter,
        CancellationToken cancellationToken)
    {
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                request,
                new ValidationContext(request),
                validationResults,
                validateAllProperties: true)
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out var recipeUri))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["url"] = ["Enter a valid public HTTP or HTTPS recipe page URL."]
            });
        }

        try
        {
            RecipeUrlImporter.ValidateRecipeUri(recipeUri);
            var draft = await recipeUrlImporter.ImportAsync(recipeUri, cancellationToken);
            return Results.Ok(RecipeDraftResponse.FromDraft(draft));
        }
        catch (RecipeImportException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The recipe page could not be imported.",
                detail: exception.Message);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The recipe website could not be reached.",
                detail: "Check the URL and try again.");
        }
        catch (IOException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The recipe page could not be read.",
                detail: "Check the URL and try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "The recipe website took too long to respond.",
                detail: "Try again later.");
        }
        catch (ArgumentException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["url"] = ["Enter a valid public HTTP or HTTPS recipe page URL."]
            });
        }
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
