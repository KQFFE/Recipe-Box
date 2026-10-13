# Recipe Box

Recipe Box is the starting point for a personal recipe-capturing and management app. The first release is a responsive ASP.NET Core web app: signed-in users can keep their own recipes, with ingredients and preparation steps stored separately.

The project is intentionally growing through small, testable workflows. Recipe photo OCR and website import both have review-before-save steps. Users can customize the order and width of a recipe's presentation sections, saved per user and recipe, and share that view through a revocable read-only link.

## What works today

- ASP.NET Core Razor Pages web app targeting .NET 10.
- ASP.NET Core Identity sign-in and account-scoped recipe access.
- Create, list, view, edit, and delete recipes.
- Separate, ordered ingredient and preparation-step lists.
- Optional description and HTTP/HTTPS image URL or uploaded cover image.
- Validation rejects recipes without at least one ingredient and preparation step, unsafe image URL schemes, and ingredient or instruction text longer than 10,000 characters.
- Capture a recipe photo from a phone or choose an image file; English and Swedish OCR extract a draft for review before saving.
- OCR runs locally with Tesseract. Uploaded photos are processed in memory and are not retained; only the user-reviewed recipe text is saved.
- Photo uploads accept JPEG, PNG, or WebP images up to 10 MB.
- Upload a recipe cover image from create/edit; validated files are stored outside the web root, private to the owner, and removed when replaced or when the recipe is deleted. A read-only share link can display its recipe's uploaded image.
- Import a public recipe page URL; Schema.org Recipe JSON-LD fields (title, description, image URL, ingredients, and steps) are extracted into an editable review form.
- URL import accepts HTTP/HTTPS pages up to 2 MB, uses a 15-second request timeout, limits redirects, and blocks connections to non-public IP ranges. Pages without supported structured recipe data show an actionable error instead of being saved.
- Customize each recipe with a live preview, drag-and-drop section ordering, accessible move buttons, and full- or half-width layout. The preference is stored separately from recipe content and scoped to both recipe and owner; layouts respond to phone-sized screens.
- Create a revocable, read-only link for a recipe. Anyone with the link can view the recipe and its current saved layout, but cannot edit it. The link remains active until revoked or replaced. It is a bearer secret; only its SHA-256 hash is stored, and creating a replacement invalidates the previous link.
- SQLite persistence and Entity Framework Core migrations.
- Unit tests for recipe text normalization, draft parsing, form validation, image upload validation and local storage, structured recipe parsing, share-token behavior, and public IP ranges; an OCR test exercises the real local engine.
- HTTP-level tests for CRUD, invalid submissions, image upload/replacement/removal, owner-only image access, photo and URL import review/save, share-link creation/revocation, authentication, and account ownership.
- Responsive page layout for desktop and phone browsers, including the live recipe-layout preview.

The app uses the standard ASP.NET Core Identity UI. Account confirmation is required by the current Identity configuration; configure and verify an email sender before relying on real account registration and confirmation flows.

### OCR runtime

The app uses the `TesseractOCR` .NET wrapper and includes Tesseract's English and Swedish `tessdata_fast` models under `src\RecipeManager.Web\tessdata`. Both are pinned to source revision `87416418657359cb625c412a48b6e1d6d41c29bd` (English SHA-256 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`; Swedish SHA-256 `F7304988D41F833EFEBCC2D529DF54B1903ECEBBC3DA1FAABD19A0FDDD4FE586`); the Apache 2.0 license is included alongside them. The test OCR image and its license are in `tests\RecipeManager.Tests\TestData`.

On Windows, the Tesseract package requires the Microsoft Visual C++ 2022 x64 runtime. On Linux or macOS, install the native Tesseract/Leptonica dependencies required by the wrapper for that host before using OCR. OCR recognizes English and Swedish text; symbols and decorative icons can still be misread, so the extracted draft must be reviewed before saving.

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

Open the local URL printed by `dotnet run`. The app uses ASP.NET Core Identity and the default SQLite connection in `src\RecipeManager.Web\appsettings.json`. The connection string is `DataSource=app.db;Cache=Shared`, which stores local account and recipe data in `src\RecipeManager.Web\app.db` when run from the repository root. The database is local-only and ignored by Git; it is not copied into build or publish output. Uploaded cover images are stored outside the web root in `src\RecipeManager.Web\App_Data\recipe-images` by default (or in the configured `RecipeImages:StoragePath`). This directory is ignored by Git. Do not commit or upload database or image-storage files because they contain account and recipe data.

Local filesystem storage is intended for development and single-server experiments. Before a multi-instance or production deployment, configure persistent private storage with appropriate backup/access controls or replace `IRecipeImageStorage` with an object-storage implementation.

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

The standard command builds into the projects' normal `bin` folders. On Windows, if the development web app is already running from that build output, it holds `RecipeManager.Web.exe` open and prevents the build from replacing it. The resulting `MSB3026` retry warnings followed by `MSB3027`/`MSB3021` copy errors indicate this file lock; they are not test assertion failures. Stop the app with `Ctrl+C` in its terminal (or stop debugging), then rerun the standard command.

To keep the app running while testing, use the repository script instead:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test.ps1
```

The script builds and runs the full solution test suite with a unique temporary output directory, then removes that directory and returns the test command's exit code. This avoids overwriting the executable currently in use. The script requires PowerShell and the .NET SDK; it does not stop or modify the running app or its database. Use the standard command when the app is stopped.

The solution includes real-browser tests. Install Chromium once after building the browser-test project:

```powershell
dotnet build tests\RecipeManager.BrowserTests\RecipeManager.BrowserTests.csproj
powershell -ExecutionPolicy Bypass -File tests\RecipeManager.BrowserTests\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet test tests\RecipeManager.BrowserTests\RecipeManager.BrowserTests.csproj
```

Browser tests launch the web app on a temporary local port with a temporary SQLite database and remove that database afterward. They register a fresh account per journey and use deterministic OCR and recipe-import adapters only when the `RecipeBox:BrowserTestMode` setting is enabled in Development. Normal Development and Production runs use local Tesseract and the public-Internet URL importer. Never enable browser-test mode in a deployed application.

Build without running tests:

```powershell
dotnet build RecipeManager.slnx
```

`RecipeContentTests` are unit tests for recipe text handling, section parsing, and form validation. `RecipeLayoutTests` cover default settings and reject invalid section configurations; `RecipeLayoutPagesTests` cover the live recipe preview, saved layout order/width, content preservation, and owner isolation. `LocalRecipeImageStorageTests` cover private per-owner file storage; `RecipeImagePagesTests` cover upload, replacement, removal, owner authorization, and share-link image access. `RecipeSharingTests` cover token generation, owner checks, read-only shared rendering with the saved layout, link rotation, and revocation. `RecipeUrlImporterTests` cover Schema.org parsing, bounded HTML retrieval, and public IP range checks. `RecipeImageUploadTests` cover upload signatures and size limits. `TesseractRecipeTextExtractorTests` exercise the actual local OCR engine against a small licensed fixture. `RecipePagesEndToEndTests` use ASP.NET Core's in-memory test host, fake OCR and URL-import boundaries, and an isolated temporary SQLite database to exercise HTTP page requests, photo and URL capture/review/save, authentication, and owner isolation. `RecipeManager.BrowserTests` use Playwright and Chromium against a separately launched web app to cover account registration, recipe CRUD, cover-image upload and share access, photo review/save, URL-import review/save, drag-and-drop layout customization with a live preview, and sharing in a real browser.

## Project structure

```text
RecipeManager.slnx
src/
  RecipeManager.Web/
    Areas/Identity/       ASP.NET Core Identity UI integration
    Data/                 EF Core DbContext and database migrations
    App_Data/             Private local recipe-image files (ignored by Git)
    Models/               Recipe, layout-preference, and share models and shared content rules
    Services/             OCR, URL import, share-token, and recipe-image storage boundaries
    Pages/
      Recipes/            Recipe list, details, capture/import review, layout, sharing, create, edit, and delete pages
      Shared/             Shared page layout and navigation
    wwwroot/               Static CSS, JavaScript, and browser libraries
    tessdata/              Pinned OCR language model and license
    Program.cs             Dependency injection and HTTP pipeline configuration
    appsettings*.json      Local application configuration
tests/
  RecipeManager.Tests/    Unit tests and HTTP-level application tests
    TestData/              Licensed OCR fixture and expected text
  RecipeManager.BrowserTests/ Playwright browser journeys against a local app process
dotnet-tools.json         Pinned local development tools
```

### Where new code should go

- **Recipe persistence fields and shared recipe data rules:** `Models/`, with database configuration in `Data/ApplicationDbContext.cs`; add a migration when the schema changes.
- **Recipe capture and OCR:** `Pages/Recipes/Capture.*` for the authenticated review-before-save flow, `Services/IRecipeTextExtractor.cs` for the replaceable OCR boundary, and `Models/RecipeDraftParser.cs` for the testable English section-heading heuristic.
- **Recipe website import:** `Pages/Recipes/Import.*` for the authenticated review-before-save flow, `Services/IRecipeUrlImporter.cs` for the replaceable website boundary, `Services/RecipeUrlImporter.cs` for bounded HTML retrieval, and `Models/RecipeStructuredDataParser.cs` for Schema.org JSON-LD extraction. The importer fetches remote pages, so retain the URL, size, redirect, timeout, and public-address checks if changing its implementation.
- **Recipe layouts:** `Pages/Recipes/Layout.*` for the per-recipe editor and live preview, `wwwroot/js/site.js` for drag-and-drop ordering and immediate width updates, `Models/RecipeLayoutPreference.cs` for separately persisted section order and width settings, and the `Details` page for rendering those sections. Keep layout state separate from recipe text, retain accessible move buttons, and validate every section against the recipe's available content.
- **Recipe sharing:** `Pages/Recipes/Share.*` manages a recipe owner's link; `Pages/Recipes/Shared.*` is the anonymous read-only view; `Services/RecipeShareToken.cs` generates high-entropy URL-safe tokens and hashes them before persistence. The shared page reuses `_RecipeContent.cshtml` and the owner's saved layout. Treat each share URL as a password: anyone who has it can view the recipe, and revoking or replacing the link stops access.
- **Recipe cover images:** `Services/IRecipeImageStorage.cs` defines the storage boundary; `Services/LocalRecipeImageStorage.cs` is the private local-filesystem implementation. `Pages/Recipes/Image.*` only serves an image to its recipe owner or through an active share link. Keep image data out of the database and public static files, validate content signatures and size at `Services/RecipeImageUpload.cs`, and clean up replaced/deleted images.
- **Upload constraints:** `Services/RecipeImageUpload.cs`; keep file-size and format checks at the upload boundary.
- **A recipe screen or page-specific form model:** `Pages/Recipes/`, keeping the `.cshtml` view beside its Razor Page model.
- **Shared navigation or page chrome:** `Pages/Shared/`.
- **Application-wide service registration or middleware:** `Program.cs`.
- **User-facing styles and scripts:** `wwwroot/css/` and `wwwroot/js/`.
- **Business-rule unit tests:** `tests/RecipeManager.Tests/`.
- **HTTP, persistence, and authorization behavior:** add focused tests in `RecipePagesEndToEndTests.cs` or a new test class using `RecipeAppFactory`.

Keep recipes scoped to the authenticated Identity user. Page handlers should query and update a recipe using both its ID and the current user's ID; never trust an owner ID supplied by a form.

## Scope and next increments

Recipe photos are OCR'd locally into an editable draft; the OCR source photo and unreviewed OCR output are not persisted. Website import reads Schema.org Recipe JSON-LD and extracts the page's declared image URL; it does not download image files or preserve the source URL as recipe metadata. In both workflows, users review and can correct extracted recipe fields before saving. Separately, recipe owners can upload an optional JPEG, PNG, or WebP cover image up to 10 MB. Image bytes are stored outside the web root behind `IRecipeImageStorage`; the default local provider stores files in ignored `App_Data` and serves them only to the owner or through an active share link. This local provider is not a multi-server production-storage strategy. Users can customize the order and widths of the description, image, ingredients, and preparation sections per recipe with a live preview, drag-and-drop, and accessible move buttons; this changes only presentation, not saved content. Layouts adapt to phone-sized screens. An owner can create a revocable read-only share link; anyone with the link can view the recipe using the owner's current layout, but cannot edit the recipe. New links rotate the secret token, and the stored database value is a hash rather than the bearer token. Real-browser tests cover CRUD, cover-image upload and sharing, photo capture, URL import, drag-and-drop layout customization, and sharing. The next planned increment is:

1. Evaluate web image search or AI image generation, with explicit privacy, licensing, attribution, and cost requirements before selecting a provider.

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
