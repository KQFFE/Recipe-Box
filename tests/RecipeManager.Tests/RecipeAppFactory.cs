using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RecipeManager.Web.Data;
using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class RecipeAppFactory : WebApplicationFactory<Program>
{
    public const string TestUserId = "recipe-test-user";

    public TestRecipeTextExtractor TextExtractor { get; } = new();

    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"recipe-manager-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
            services.RemoveAll<IRecipeTextExtractor>();
            services.AddSingleton<IRecipeTextExtractor>(TextExtractor);

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.AuthenticationScheme,
                    _ => { });
        });
    }

    public HttpClient CreateAppClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();

        if (!db.Users.Any(user => user.Id == TestUserId))
        {
            db.Users.Add(new IdentityUser
            {
                Id = TestUserId,
                UserName = "recipe-owner@example.test",
                NormalizedUserName = "RECIPE-OWNER@EXAMPLE.TEST",
                Email = "recipe-owner@example.test",
                NormalizedEmail = "RECIPE-OWNER@EXAMPLE.TEST",
                EmailConfirmed = true
            });
            db.SaveChanges();
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

public sealed class TestRecipeTextExtractor : IRecipeTextExtractor
{
    public string Text { get; set; } =
        "Tomato soup\nIngredients:\n4 tomatoes\n1 onion\nInstructions:\nRoast the vegetables.\nBlend until smooth.";

    public int CallCount { get; private set; }

    public int LastImageLength { get; private set; }

    public string ExtractText(byte[] imageBytes)
    {
        CallCount++;
        LastImageLength = imageBytes.Length;
        return Text;
    }
}

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "RecipeTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Unauthenticated"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, RecipeAppFactory.TestUserId)],
            AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
