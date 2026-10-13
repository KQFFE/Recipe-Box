# Recipe Box

Recipe Box is the starting point for a personal recipe-capturing and management app. The first release is a responsive ASP.NET Core web app: signed-in users can keep their own recipes, with ingredients and preparation steps stored separately.

The project is intentionally growing through small, testable workflows. Recipe photo OCR and review-before-save are implemented. Recipe website import, custom layouts, sharing, and recipe-image storage are future work.

## What works today

- ASP.NET Core Razor Pages web app targeting .NET 10.
- ASP.NET Core Identity sign-in and account-scoped recipe access.
- Create, list, view, edit, and delete recipes.
- Separate, ordered ingredient and preparation-step lists.
- Optional description and HTTP/HTTPS image URL.
- Validation rejects recipes without at least one ingredient and preparation step, unsafe image URL schemes, and ingredient or instruction text longer than 10,000 characters.
- Capture a recipe photo from a phone or choose an image file; English OCR extracts a draft for review before saving.
- OCR runs locally with Tesseract. Uploaded photos are processed in memory and are not retained; only the user-reviewed recipe text is saved.
- Photo uploads accept JPEG, PNG, or WebP images up to 10 MB.
- SQLite persistence and Entity Framework Core migrations.
- Unit tests for recipe text normalization, draft parsing, form validation, and upload validation; an OCR test exercises the real local engine.
- HTTP-level tests for CRUD, invalid submissions, photo capture/review/save, authentication, and account ownership.
- Responsive page layout for desktop and phone browsers.

The app uses the standard ASP.NET Core Identity UI. Account confirmation is required by the current Identity configuration; configure and verify an email sender before relying on real account registration and confirmation flows.

### OCR runtime

The app uses the `TesseractOCR` .NET wrapper and includes Tesseract's English `tessdata_fast` model under `src\RecipeManager.Web\tessdata`. The model is pinned to source revision `87416418657359cb625c412a48b6e1d6d41c29bd` (SHA-256 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`); its Apache 2.0 license is included alongside it. The test OCR image and its license are in `tests\RecipeManager.Tests\TestData`.

On Windows, the Tesseract package requires the Microsoft Visual C++ 2022 x64 runtime. On Linux or macOS, install the native Tesseract/Leptonica dependencies required by the wrapper for that host before using OCR. OCR is English-only in this first slice.

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

Open the local URL printed by `dotnet run`. The app uses ASP.NET Core Identity and the default SQLite connection in `src\RecipeManager.Web\appsettings.json`. The connection string is `DataSource=app.db;Cache=Shared`, which stores local account and recipe data in `src\RecipeManager.Web\app.db` when run from the repository root. The database is local-only and ignored by Git; it is not copied into build or publish output. Do not commit or upload database files because they contain account and recipe data.

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

`RecipeContentTests` are unit tests for recipe text handling, section parsing, and form validation. `RecipeImageUploadTests` cover upload signatures and size limits. `TesseractRecipeTextExtractorTests` exercise the actual local OCR engine against a small licensed fixture. `RecipePagesEndToEndTests` use ASP.NET Core's in-memory test host, a fake OCR boundary, and an isolated temporary SQLite database to exercise HTTP page requests, invalid submissions, photo capture/review/save, authentication, and owner isolation (including cross-account delete attempts). The test authentication handler and fake extractor are test-only; neither is registered by the web app. These are HTTP-level end-to-end tests, not real-browser Playwright tests.

## Project structure

```text
RecipeManager.slnx
src/
  RecipeManager.Web/
    Areas/Identity/       ASP.NET Core Identity UI integration
    Data/                 EF Core DbContext and database migrations
    Models/               Persisted recipe model and shared recipe-content helpers
    Services/             OCR extraction boundary and upload validation
    Pages/
      Recipes/            Recipe list, details, capture/review, create, edit, and delete pages
      Shared/             Shared page layout and navigation
    wwwroot/               Static CSS, JavaScript, and browser libraries
    tessdata/              Pinned OCR language model and license
    Program.cs             Dependency injection and HTTP pipeline configuration
    appsettings*.json      Local application configuration
tests/
  RecipeManager.Tests/    Unit tests and HTTP-level application tests
    TestData/              Licensed OCR fixture and expected text
dotnet-tools.json         Pinned local development tools
```

### Where new code should go

- **Recipe persistence fields and shared recipe data rules:** `Models/`, with database configuration in `Data/ApplicationDbContext.cs`; add a migration when the schema changes.
- **Recipe capture and OCR:** `Pages/Recipes/Capture.*` for the authenticated review-before-save flow, `Services/IRecipeTextExtractor.cs` for the replaceable OCR boundary, and `Models/RecipeDraftParser.cs` for the testable English section-heading heuristic.
- **Upload constraints:** `Services/RecipeImageUpload.cs`; keep file-size and format checks at the upload boundary.
- **A recipe screen or page-specific form model:** `Pages/Recipes/`, keeping the `.cshtml` view beside its Razor Page model.
- **Shared navigation or page chrome:** `Pages/Shared/`.
- **Application-wide service registration or middleware:** `Program.cs`.
- **User-facing styles and scripts:** `wwwroot/css/` and `wwwroot/js/`.
- **Business-rule unit tests:** `tests/RecipeManager.Tests/`.
- **HTTP, persistence, and authorization behavior:** add focused tests in `RecipePagesEndToEndTests.cs` or a new test class using `RecipeAppFactory`.

Keep recipes scoped to the authenticated Identity user. Page handlers should query and update a recipe using both its ID and the current user's ID; never trust an owner ID supplied by a form.

## Scope and next increments

Recipe photos are OCR'd locally into an editable draft; no photo file or unreviewed OCR output is persisted. The recipe form validates required content and image URL schemes, and tests cover invalid create/edit submissions and cross-account read/delete boundaries. Real-browser tests remain future work. The near-term goal is to grow the app in independent, testable vertical slices:

1. Add real-browser tests for the important recipe and photo-capture journeys.
2. Add recipe URL import with structured extraction, clear error states, and a review step.
3. Add flexible layouts as saved presentation preferences separate from the recipe's ingredients and instructions.
4. Add controlled recipe sharing, with explicit decisions about what is shared and whether the recipient can edit.
5. Add optional recipe-image storage or image selection/generation behind replaceable boundaries, after deciding privacy, licensing, attribution, and cost requirements.

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
