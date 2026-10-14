using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RecipeBox.Web.Services;

public static class OpenIddictApplicationSeeder
{
    public const string MobileClientId = "recipebox-mobile";
    public const string MobileRedirectUri = "com.kqffe.recipebox://callback";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopeManager.FindByNameAsync("recipes.read", cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "recipes.read",
                DisplayName = "Read recipes",
                Resources = { "recipebox-api" }
            }, cancellationToken);
        }

        if (await scopeManager.FindByNameAsync("recipes.write", cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "recipes.write",
                DisplayName = "Create and update recipes",
                Resources = { "recipebox-api" }
            }, cancellationToken);
        }

        var applicationManager = services.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applicationManager.FindByClientIdAsync(MobileClientId, cancellationToken) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = MobileClientId,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "Recipe Box mobile app",
                RedirectUris = { new Uri(MobileRedirectUri) },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + "recipes.read",
                    Permissions.Prefixes.Scope + "recipes.write"
                }
            }, cancellationToken);
        }
    }
}
