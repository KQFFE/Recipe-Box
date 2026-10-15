using System.Net;
using System.Text.RegularExpressions;

namespace RecipeBox.Tests;

public sealed class MobileAccountRegistrationTests
{
    [Fact]
    public async Task RegisterAndConfirmEmail_PreservesMobileAuthorizationReturnUrl()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        const string returnUrl =
            "/connect/authorize?client_id=recipebox-mobile&response_type=code&state=mobile-state";

        var loginPage = await client.GetAsync(
            $"/Identity/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        Assert.Contains("/Identity/Account/Register?returnUrl=%2Fconnect%2Fauthorize", loginHtml);

        var registerPage = await client.GetAsync(
            $"/Identity/Account/Register?returnUrl={Uri.EscapeDataString(returnUrl)}");
        Assert.Equal(HttpStatusCode.OK, registerPage.StatusCode);
        var registerHtml = await registerPage.Content.ReadAsStringAsync();
        var antiforgeryToken = Regex.Match(
            registerHtml,
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
            .Groups[1]
            .Value;
        Assert.False(string.IsNullOrWhiteSpace(antiforgeryToken), registerHtml);

        var registrationResponse = await client.PostAsync(
            $"/Identity/Account/Register?returnUrl={Uri.EscapeDataString(returnUrl)}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["Input.Email"] = "mobile-register@example.test",
                ["Input.Password"] = "Test-password-123!",
                ["Input.ConfirmPassword"] = "Test-password-123!"
            }));

        Assert.Equal(HttpStatusCode.Redirect, registrationResponse.StatusCode);
        var registrationLocation = registrationResponse.Headers.Location?.OriginalString;
        Assert.Contains("returnUrl", registrationLocation);
        Assert.Contains("mobile-state", registrationLocation);
        Assert.Contains("ConfirmEmail", factory.EmailSender.LastMessage);

        var confirmationLink = WebUtility.HtmlDecode(
            Regex.Match(factory.EmailSender.LastMessage!, "href=\"([^\"]+)\"").Groups[1].Value);
        var confirmationResponse = await client.GetAsync(confirmationLink);

        Assert.Equal(HttpStatusCode.Redirect, confirmationResponse.StatusCode);
        Assert.Contains("/Identity/Account/Login", confirmationResponse.Headers.Location?.OriginalString);
        Assert.Contains("returnUrl=%2Fconnect%2Fauthorize", confirmationResponse.Headers.Location?.OriginalString);
        Assert.Contains("mobile-state", confirmationResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Register_RejectsExternalReturnUrl()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();

        var response = await client.GetAsync(
            "/Identity/Account/Register?returnUrl=https%3A%2F%2Fattacker.example%2F");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("attacker.example", await response.Content.ReadAsStringAsync());
    }
}
