# Recipe Box

Recipe Box is the starting point for a personal recipe-capturing and management app. The first release is a responsive ASP.NET Core web app: signed-in users can keep their own recipes, with ingredients and preparation steps stored separately.

The project is intentionally starting with a small, testable foundation. Recipe photo OCR, importing from recipe websites, custom recipe layouts, and sharing are future work; they are not implemented yet.

## What works today

- ASP.NET Core Razor Pages web app targeting .NET 10.
- ASP.NET Core Identity sign-in and account-scoped recipe access.
- Create, list, view, edit, and delete recipes.
- Separate, ordered ingredient and preparation-step lists.
- Optional description and image URL.
- SQLite persistence and Entity Framework Core migrations.
- Unit tests for recipe text normalization and HTTP-level tests for the recipe CRUD flow, authentication, and ownership.
- Responsive page layout for desktop and phone browsers.

The app uses the standard ASP.NET Core Identity UI. Account confirmation is required by the current Identity configuration; configure and verify an email sender before relying on real account registration and confirmation flows.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (not just the runtime).
- Windows, macOS, or Linux.

Check that the SDK is installed:

```powershell
dotnet --list-sdks
```

The repository includes a local .NET tool manifest pinning Entity Framework Core CLI (`dotnet-ef`) to the same version as the app's EF Core packages.

## Run locally

From the repository root:

```powershell
dotnet restore RecipeManager.slnx
dotnet run --project src\RecipeManager.Web\RecipeManager.Web.csproj
```

Open the local URL printed by `dotnet run`. The app uses ASP.NET Core Identity and the default SQLite connection in `src\RecipeManager.Web\appsettings.json`. The connection string is `DataSource=app.db;Cache=Shared`; the database file is local development data and is ignored by Git.

In Development, the app applies pending EF Core migrations at startup. Do not rely on automatic startup migrations for production deployments; apply reviewed migrations through a deliberate deployment step instead.

To stop the app, press `Ctrl+C` in its terminal.

### Database migrations

Restore the repository's local tools once after cloning:

```powershell
dotnet tool restore
```

After changing the EF Core model, create and review a migration:

```powershell
dotnet tool run dotnet-ef migrations add DescribeTheChange --project src\RecipeManager.Web\RecipeManager.Web.csproj
```

Apply migrations to the configured database:

```powershell
dotnet tool run dotnet-ef database update --project src\RecipeManager.Web\RecipeManager.Web.csproj
```

Keep migration files in `src\RecipeManager.Web\Data\Migrations`. Never commit local database files, credentials, or production connection strings.

## Run tests

Run all tests from the repository root:

```powershell
dotnet test RecipeManager.slnx
```

Build without running tests:

```powershell
dotnet build RecipeManager.slnx
```

`RecipeContentTests` are unit tests. `RecipePagesEndToEndTests` use ASP.NET Core's in-memory test host and a temporary SQLite database to exercise HTTP page requests, form posts, authentication, and owner isolation. The test authentication handler is test-only; it is not registered by the web app. These are HTTP-level end-to-end tests, not real-browser Playwright tests.

## Project structure

```text
RecipeManager.slnx
src/
  RecipeManager.Web/
    Areas/Identity/       ASP.NET Core Identity UI integration
    Data/                 EF Core DbContext and database migrations
    Models/               Persisted recipe model and shared recipe-content helpers
    Pages/
      Recipes/            Recipe list, details, create, edit, and delete pages
      Shared/             Shared page layout and navigation
    wwwroot/               Static CSS, JavaScript, and browser libraries
    Program.cs             Dependency injection and HTTP pipeline configuration
    appsettings*.json      Local application configuration
tests/
  RecipeManager.Tests/    Unit tests and HTTP-level application tests
dotnet-tools.json         Pinned local development tools
```

### Where new code should go

- **Recipe persistence fields and shared recipe data rules:** `Models/`, with database configuration in `Data/ApplicationDbContext.cs`; add a migration when the schema changes.
- **A recipe screen or page-specific form model:** `Pages/Recipes/`, keeping the `.cshtml` view beside its Razor Page model.
- **Shared navigation or page chrome:** `Pages/Shared/`.
- **Application-wide service registration or middleware:** `Program.cs`.
- **User-facing styles and scripts:** `wwwroot/css/` and `wwwroot/js/`.
- **Business-rule unit tests:** `tests/RecipeManager.Tests/`.
- **HTTP, persistence, and authorization behavior:** add focused tests in `RecipePagesEndToEndTests.cs` or a new test class using `RecipeAppFactory`.

Keep recipes scoped to the authenticated Identity user. Page handlers should query and update a recipe using both its ID and the current user's ID; never trust an owner ID supplied by a form.

## Scope and next increments

The near-term goal is to grow the app in independent, testable vertical slices:

1. Improve recipe editing and validation, then add browser-based tests for important user journeys.
2. Add photo upload and OCR as an extraction-and-review flow before saving any recognized text.
3. Add recipe URL import with structured extraction, clear error states, and a review step.
4. Add flexible layouts as saved presentation preferences separate from the recipe's ingredients and instructions.
5. Add controlled recipe sharing, with explicit decisions about what is shared and whether the recipient can edit.
6. Add image selection or generation behind a replaceable provider, with licensing, attribution, and cost considerations addressed before use.

Each increment should include unit tests for its rules, HTTP or integration tests for persistence and account boundaries, and browser-level end-to-end coverage for its main user journey.

## Testability and growth

The current app is a small ASP.NET Core application with clear boundaries between Razor Pages, recipe models, persistence, and tests. The recipe database context is replaceable through dependency injection, and the current HTTP tests use their own temporary database instead of developer data.

SQLite is a simple local starting point, not a scale target for every deployment. As usage and operational needs grow:

- Move to a managed relational database (for example PostgreSQL or SQL Server) and select the matching EF Core provider.
- Keep data access behind application-level operations/services if business workflows expand beyond page handlers.
- Store uploaded images in object storage rather than in the relational database or on an individual app server.
- Run OCR and URL import as isolated services or background jobs when work becomes slow, costly, or needs retries.
- Version saved layouts independently from recipe content so layout changes do not corrupt ingredient or instruction data.
- Add authorization and sharing policies, observability, backups, and deployment-managed migrations before production use.

These are design directions, not infrastructure already present in this repository. Keep additions proportional to demonstrated needs; do not introduce distributed services or cloud dependencies before a feature needs them.
