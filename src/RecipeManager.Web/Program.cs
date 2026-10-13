using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RecipeManager.Web.Data;
using RecipeManager.Web.Services;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var isBrowserTesting = builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("RecipeBox:BrowserTestMode", false);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
        options.SignIn.RequireConfirmedAccount = !isBrowserTesting)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddRazorPages();
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
if (app.Environment.IsDevelopment() || isBrowserTesting)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
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
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program { }
