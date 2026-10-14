using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using RecipeBox.Web.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RecipeBox.Web.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class AuthorizationController(UserManager<IdentityUser> userManager) : Controller
{
    [AcceptVerbs("GET", "POST")]
    [Route("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeAsync()
    {
        var request = OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(HttpContext)
            ?? throw new InvalidOperationException("The OpenID Connect authorization request is unavailable.");
        if (!string.Equals(request.ClientId, OpenIddictApplicationSeeder.MobileClientId, StringComparison.Ordinal))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            if (request.HasPromptValue("none"))
            {
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id);
        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            identity.SetClaim(Claims.Name, user.UserName);
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.SetClaim(Claims.Email, user.Email);
        }

        identity.SetScopes(request.GetScopes());
        identity.SetResources("recipebox-api");
        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name when identity.HasScope(Scopes.Profile) =>
                [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Email when identity.HasScope(Scopes.Email) =>
                [Destinations.AccessToken, Destinations.IdentityToken],
            _ => []
        });

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
