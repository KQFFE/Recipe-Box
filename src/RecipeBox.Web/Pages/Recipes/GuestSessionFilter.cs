using Microsoft.AspNetCore.Mvc.Filters;
using RecipeBox.Web.Services;

namespace RecipeBox.Web.Pages.Recipes;

// Lets visitors without an account create, import and capture recipes: the first save creates a guest profile.
public sealed class GuestSessionFilter : IAsyncPageFilter
{
    private static readonly HashSet<string> GuestPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/Recipes/Create",
        "/Recipes/Import",
        "/Recipes/Capture",
    };

    public async Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true
            && HttpMethods.IsPost(http.Request.Method)
            && GuestPages.Contains(context.ActionDescriptor.ViewEnginePath))
        {
            await http.RequestServices.GetRequiredService<GuestAccountService>().CreateAndSignInAsync(http);
        }
    }

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next) =>
        next();
}
