using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using RecipeBox.Web.Api;
using RecipeBox.Web.Data;
using RecipeBox.Web.Services;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var isBrowserTesting = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("RecipeBox:BrowserTestMode", false);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(connectionString);
    options.UseOpenIddict();
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
        options.SignIn.RequireConfirmedAccount = !isBrowserTesting)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddRazorPages();
builder.Services.AddControllers();

var issuer = builder.Configuration["Authentication:Issuer"];
var useDevelopmentKeys = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var requiresProductionKeys = !useDevelopmentKeys;
if (requiresProductionKeys
    && (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
        || issuerUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        "Authentication:Issuer must be an absolute HTTPS URL outside Development.");
}

var signingCertificatePath = builder.Configuration["OpenIddict:SigningCertificatePath"];
var signingCertificatePassword = builder.Configuration["OpenIddict:SigningCertificatePassword"];
var encryptionCertificatePath = builder.Configuration["OpenIddict:EncryptionCertificatePath"];
var encryptionCertificatePassword = builder.Configuration["OpenIddict:EncryptionCertificatePassword"];
if (requiresProductionKeys
    && (string.IsNullOrWhiteSpace(signingCertificatePath)
        || string.IsNullOrWhiteSpace(signingCertificatePassword)
        || string.IsNullOrWhiteSpace(encryptionCertificatePath)
        || string.IsNullOrWhiteSpace(encryptionCertificatePassword)))
{
    throw new InvalidOperationException(
        "OpenIddict signing and encryption certificate paths and passwords must be configured outside Development.");
}

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            options.SetIssuer(new Uri(issuer, UriKind.Absolute));
        }

        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetRevocationEndpointUris("/connect/revoke")
            .AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .RequireProofKeyForCodeExchange()
            .RegisterScopes(
                OpenIddictConstants.Scopes.Email,
                OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Scopes.OfflineAccess,
                "recipes.read",
                "recipes.write")
            .SetAccessTokenLifetime(TimeSpan.FromMinutes(10))
            .SetRefreshTokenLifetime(TimeSpan.FromDays(30));

        if (useDevelopmentKeys)
        {
            options.AddDevelopmentSigningCertificate()
                .AddDevelopmentEncryptionCertificate();
        }
        else
        {
            var signingCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                signingCertificatePath!,
                signingCertificatePassword!);
            var encryptionCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                encryptionCertificatePath!,
                encryptionCertificatePassword!);
            options.AddSigningCertificate(signingCertificate)
                .AddEncryptionCertificate(encryptionCertificate);
        }

        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough();
        if (useDevelopmentKeys)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    })
    .AddValidation(options =>
    {
        options.AddAudiences("recipebox-api");
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RecipeRead", policy =>
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasScope("recipes.read")));
    options.AddPolicy("RecipeWrite", policy =>
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasScope("recipes.write")));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("recipe-api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
var imageStoragePath = builder.Configuration["RecipeImages:StoragePath"];
builder.Services.AddSingleton<IRecipeImageStorage>(_ =>
    new LocalRecipeImageStorage(
        string.IsNullOrWhiteSpace(imageStoragePath)
            ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "recipe-images")
            : Path.GetFullPath(imageStoragePath, builder.Environment.ContentRootPath)));
if (isBrowserTesting)
{
    builder.Services.AddSingleton<IRecipeTextExtractor, BrowserTestRecipeTextExtractor>();
    builder.Services.AddSingleton<IRecipeUrlImporter, BrowserTestRecipeUrlImporter>();
}
else
{
    builder.Services.AddScoped<IRecipeTextExtractor>(services =>
    {
        var environment = services.GetRequiredService<IWebHostEnvironment>();
        var tessdataPath = Path.Combine(environment.ContentRootPath, "tessdata");
        return new TesseractRecipeTextExtractor(tessdataPath);
    });
    builder.Services.AddHttpClient<IRecipeUrlImporter, RecipeUrlImporter>(client =>
        client.Timeout = TimeSpan.FromSeconds(15))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = RecipeUrlImporter.ConnectToPublicAddressAsync,
            MaxAutomaticRedirections = 3,
            UseProxy = false
        });
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing") || isBrowserTesting)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
}

using (var scope = app.Services.CreateScope())
{
    await OpenIddictApplicationSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapControllers();
app.MapRecipesApi();

app.Run();

public partial class Program { }
