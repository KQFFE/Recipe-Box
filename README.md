# Recipe Box

Recipe Box is the starting point for a personal recipe-capturing and management app. The first release is a responsive ASP.NET Core web app: signed-in users can keep their own recipes, with ingredients and preparation steps stored separately.

The project is intentionally growing through small, testable workflows. Recipe photo OCR and website import both have review-before-save steps. Users can customize the order and width of a recipe's presentation sections, saved per user and recipe, and share that view through a revocable read-only link.

The website supports phone-sized screens and can be used in a mobile browser. Native Android and iOS apps are in progress; a PWA is not a required part of that work. See [Roadmap.md](./Roadmap.md).

A .NET MAUI app in `src\RecipeBox.Mobile` now signs in through the system browser and can browse and create recipes through the authenticated API. See the [mobile development setup](#native-mobile-development-prerequisites) before running it on a device.

## Accessibility standard

WCAG 2.1 Level AA is a product requirement for every page and future change. Preserve semantic headings and landmarks, programmatic form labels and error messages, keyboard operation and visible focus, readable text/background contrast, and usable pointer targets. Do not rely on color, dragging, or hover alone to communicate information or complete an action. Prefer native accessible HTML controls and provide equivalent keyboard-operable actions for gestures.

Accessibility validation is part of feature acceptance: add browser coverage for the relevant keyboard, screen-reader semantics, responsive zoom/reflow, and target sizing, and run automated accessibility checks where available. Automated checks supplement, but do not replace, manual keyboard and screen-reader review. Do not describe the app as WCAG-conformant until all pages and key states have been audited against WCAG 2.1 AA.

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
- Import a public recipe page URL; Schema.org Recipe JSON-LD is preferred, with a fallback that extracts visible ingredient and instruction sections from printer-friendly recipe pages. Available title, description, and image metadata are included in the editable review form.
- Recipe capture and URL import detect English or Swedish from the recipe text, prefer a supported Schema.org `inLanguage` value when provided, and let users correct the detected language before saving. The language is saved with the recipe so ingredient and preparation headings remain consistent in recipe details and shared views; additional languages can be added to the language catalog.
- URL import accepts HTTP/HTTPS pages up to 2 MB, uses a 15-second request timeout, limits redirects, and blocks connections to non-public IP ranges. The visible-text fallback requires a page title and recognizable ingredient and instruction sections; pages without those show an actionable error instead of being saved. Pages that require JavaScript to render their recipe content may not import.
- Customize each recipe in a full-viewport live overview, with equal-height 24rem desktop and 20rem mobile cards, an expandable “How to customize” guide, scrollable full details, drag-and-drop section swapping, a visible “Put here” target, very fast edge auto-scroll while holding touch drags, accessible move buttons, and full- or half-width layout. Touch drag handles and large controls support narrow mobile screens. The preference is stored separately from recipe content and scoped to both recipe and owner.
- Create a revocable, read-only link for a recipe. Anyone with the link can view the recipe and its current saved layout, but cannot edit it. The link remains active until revoked or replaced. It is a bearer secret; only its SHA-256 hash is stored, and creating a replacement invalidates the previous link.
- SQLite persistence and Entity Framework Core migrations.
- Unit tests for recipe text normalization, draft parsing, form validation, image upload validation and local storage, structured recipe parsing, share-token behavior, and public IP ranges; an OCR test exercises the real local engine.
- HTTP-level tests for CRUD, invalid submissions, image upload/replacement/removal, owner-only image access, English and Swedish photo and URL import review/save, share-link creation/revocation, authentication, and account ownership. Mobile API/OIDC integration tests cover PKCE code exchange, bearer validation, scopes, request validation, and recipe-owner isolation.
- Responsive page layout for desktop and phone browsers, including the live recipe-layout preview.

The app uses the standard ASP.NET Core Identity UI. Account confirmation is required by the current Identity configuration; configure and verify an email sender before relying on real account registration and confirmation flows.

## Mobile API and sign-in design

The mobile app uses the existing ASP.NET Core Identity accounts and recipe database. It does not create a second account store or ask users to create a separate mobile password.

OpenIddict now provides the authorization-code issuer and local bearer-token validation. The first version of the recipe API is available under `/api/v1/recipes`; it supports paged list, detail, create, update, and delete operations. Unit/HTTP tests exercise token issuance with PKCE, bearer-token validation, required scopes, recipe validation, and owner isolation.

### Sign-in

* The web app hosts OpenID Connect/OAuth endpoints with OpenIddict, integrated with ASP.NET Core Identity for its existing login, account-confirmation, and lockout rules. Identity remains the user store; OpenIddict persists authorization/token state through EF Core.
* The native app uses the OAuth 2.0 authorization-code flow with PKCE, launched in the device's system browser. It is a public client: it contains no client secret and does not collect the Identity password in a native form.
* After sign-in, the app sends short-lived bearer access tokens to the API and silently renews them with rotating refresh tokens. Both tokens are stored with platform secure storage. Refresh tokens expire after 30 days without use, with the expiration sliding forward as the user continues using the app; the user must sign in again after prolonged inactivity or if the session is revoked.
* Sign-out revokes the server-side refresh token and clears local tokens. If the server cannot be reached, local tokens are still cleared; the server-side token then expires according to its configured lifetime.
* The current native callback is the exact registered custom URI `com.kqffe.recipebox://callback`. Prefer verified Android App Links and iOS Universal Links before production release; do not accept arbitrary redirect URIs. Require HTTPS for the issuer and API.
* The API accepts tokens from this issuer and requires the `recipebox-api` audience and appropriate scopes. A valid token identifies the owner; clients must never choose or submit an owner ID.

### Initial API contract

Expose a versioned JSON API under `/api/v1`. Use request/response DTOs rather than exposing EF Core entities. The initial mobile slice needs:

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/v1/recipes` | List the signed-in user's recipes |
| `GET` | `/api/v1/recipes/{id}` | Read one of the signed-in user's recipes |
| `POST` | `/api/v1/recipes` | Create a recipe for the signed-in user |
| `PUT` | `/api/v1/recipes/{id}` | Update one of the signed-in user's recipes |
| `DELETE` | `/api/v1/recipes/{id}` | Delete one of the signed-in user's recipes |

Recipe reads and writes filter by the authenticated user's subject/Identity ID, including when looking up a recipe by ID; both missing recipes and recipes owned by someone else return `404`. List responses default to 20 items and enforce a maximum page size of 100. Request and response DTOs represent ingredients and instructions as ordered line arrays and include language and timestamps; write requests cannot set an owner ID, storage key, or server-managed timestamp. Fields are validated and normalized using the existing web form rules. Validation failures return `ProblemDetails`.

The API also exposes `GET` and `DELETE /api/v1/recipes/{id}/image` and `POST /api/v1/recipes/{id}/image` for private cover images; every operation requires the recipe owner and the matching read/write scope. Uploads accept JPEG, PNG, or WebP up to 10 MB and replace any prior uploaded cover. Private image responses are non-cacheable. `POST /api/v1/recipes/drafts/photo` accepts a JPEG, PNG, or WebP photo up to 10 MB and returns OCR text as an editable draft without storing the photo. `POST /api/v1/recipes/drafts/import` accepts a public recipe URL and returns an editable draft; URL fetching uses the existing SSRF protections. Neither draft operation saves a recipe: the mobile user must review and explicitly save it.

Use `openid`, `profile`, `email`, and `offline_access` for sign-in and session renewal, plus `recipes.read` and `recipes.write` scopes for API permissions. `GET` operations require `recipes.read`; create, update, and delete require `recipes.write`. Native clients do not need browser-cookie authentication or CORS for their HTTP requests. Do not log bearer tokens, passwords, refresh tokens, or recipe contents. JSON requests are limited to 64 KiB, photo/image multipart requests to 11 MiB total, and API calls to 60 requests per minute per remote IP address. Use HTTPS in deployed environments.

The native app is local-first: recipes and cover images are stored in the app's private data directory, so signed-out users can browse, create, edit, and delete them offline. Photo capture/OCR and recipe-page import still require a signed-in profile and a network connection; extracted data is shown for review before saving.

### Local recipes and profile sync

* The app uses a local SQLite database and app-private image files. Android Auto Backup is disabled so local recipe data is not copied into an Android cloud backup.
* Users can create, browse, edit, and delete recipes without an account, server address, or network. Changes remain on the device until the user chooses **Save to Profile**.
* **Save to Profile** opens the existing system-browser login/registration flow. On first sync it uploads device recipes and downloads recipes and cover images from the profile. Signed-in edits and deletions queue locally while offline and sync on refresh or when the app next opens online.
* Recipe conflicts use last-edit-wins based on UTC modification times, including deletions and cover-image changes. Keep the device clock accurate for predictable conflict resolution.
* Signing out asks for confirmation, then removes profile-bound recipes and images from this device. Device-only recipes that were never promoted to a profile remain. Invalid/revoked authentication also clears profile data locally.
* No permanent production server URL is configured yet. In Debug, the app asks for an HTTPS address only when the user taps **Save to Profile**; the prompt supports local development tunnels. A Debug-only **Change dev server** action clears profile data and the saved session before switching servers. For a Release build, set the stable HTTPS host at build time:

  ```powershell
  dotnet build src\RecipeBox.Mobile\RecipeBox.Mobile.csproj --configuration Release --framework net10.0-android -p:RecipeBoxServerUrl=https://recipes.example.com
  ```

  The configured host must be the same HTTPS issuer used by the web/API server. Do not use a temporary `trycloudflare.com` tunnel for release builds.

For production, configure `Authentication__Issuer` as the stable HTTPS issuer/API address and provide separate PKCS#12 certificates using `OpenIddict__SigningCertificatePath`, `OpenIddict__SigningCertificatePassword`, `OpenIddict__EncryptionCertificatePath`, and `OpenIddict__EncryptionCertificatePassword`. Use durable protected certificate storage, plan certificate rotation, and keep the certificate files/passwords and provider credentials out of source control. Configure a verified email sender for confirmed accounts. iOS signing and App Store delivery additionally require Apple developer credentials and macOS/Xcode build infrastructure.

### OCR runtime

The app uses the `TesseractOCR` .NET wrapper and includes Tesseract's English and Swedish `tessdata_fast` models under `src\RecipeBox.Web\tessdata`. Both are pinned to source revision `87416418657359cb625c412a48b6e1d6d41c29bd` (English SHA-256 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`; Swedish SHA-256 `F7304988D41F833EFEBCC2D529DF54B1903ECEBBC3DA1FAABD19A0FDDD4FE586`); the Apache 2.0 license is included alongside them. The test OCR image and its license are in `tests\RecipeBox.Tests\TestData`.

On Windows, the Tesseract package requires the Microsoft Visual C++ 2022 x64 runtime. On Linux or macOS, install the native Tesseract/Leptonica dependencies required by the wrapper for that host before using OCR. OCR recognizes English and Swedish text; symbols and decorative icons can still be misread, so the extracted draft must be reviewed before saving.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (not just the runtime).
- Windows, macOS, or Linux.
- Git.

Check that the SDK is installed:

```powershell
dotnet --list-sdks
```

The repository includes a local .NET tool manifest pinning Entity Framework Core CLI (`dotnet-ef`) to the same version as the app's EF Core packages.

### Native mobile development prerequisites

The MAUI project targets Android and iOS. Building for a platform requires its native toolchain in addition to the .NET SDK:

* **Android:** install the MAUI workload (`dotnet workload install maui`), JDK 17, and Android SDK Platform 36 plus Build-Tools 36.0.0. Android Studio can install Android SDK components through its SDK Manager. The GitHub Actions Android job provisions these tools on its hosted runner.
* **iOS:** use a Mac with Xcode installed and configured, then install the MAUI workload (`dotnet workload install maui`). On Windows, pair Visual Studio with a reachable Mac build host; iOS apps cannot be built or signed on Windows alone.
* **Windows desktop:** the project can compile for Windows, but MAUI's `WebAuthenticator` does not support Windows sign-in. The interactive authentication journey is for Android and iOS.
* **Device distribution:** Apple signing and App Store submission require Apple developer credentials. Android release signing also requires a protected signing key; do not put signing keys or passwords in the repository.
* **Authentication/API:** no extra local secret is needed for Development or automated tests; OpenIddict generates development certificates. Production hosting must provide the HTTPS issuer and separate signing/encryption certificates listed in the [mobile API setup](#mobile-api-and-sign-in-design).
* **Running on a device:** signed-out recipe use is local and does not need a server. To sync a profile, use a reachable HTTPS server with a certificate the device trusts. `localhost` on a phone refers to the phone, not the development computer. Do not disable TLS certificate validation or use an untrusted certificate to work around connection errors.
* **Local server certificates:** the ASP.NET Core Development certificate is intended for local development and is not automatically trusted by Android or iOS devices. For device testing, use a trusted development certificate and a hostname/address reachable from the device, or use the documented HTTPS tunnel. The app intentionally accepts HTTPS addresses only.
* **Sign-in behavior:** the app requests `openid`, `profile`, `email`, `offline_access`, `recipes.read`, and `recipes.write`. Access tokens expire after 10 minutes and are refreshed in the background while the refresh session remains valid. Refresh tokens rotate and expire after 30 days of inactivity. The app's Android intent filter and iOS URL scheme must continue to match the exact registered redirect URI.
* **Camera:** on first use, grant camera access when prompted. Denying it leaves URL import and manual recipe entry available.

After installing prerequisites, verify the Android target can be built:

```powershell
dotnet workload list
dotnet build src\RecipeBox.Mobile\RecipeBox.Mobile.csproj --configuration Release --framework net10.0-android
```

On a configured Mac, build the iOS target with:

```bash
dotnet workload list
dotnet build src/RecipeBox.Mobile/RecipeBox.Mobile.csproj --configuration Release --framework net10.0-ios
```

## Run locally

From the repository root:

```powershell
dotnet restore src\RecipeBox.Web\RecipeBox.Web.csproj
dotnet run --project src\RecipeBox.Web\RecipeBox.Web.csproj
```

Open the local URL printed by `dotnet run`. The app uses ASP.NET Core Identity and the default SQLite connection in `src\RecipeBox.Web\appsettings.json`. The connection string is `DataSource=app.db;Cache=Shared`, which stores local account and recipe data in `src\RecipeBox.Web\app.db` when run from the repository root. The database is local-only and ignored by Git; it is not copied into build or publish output. Uploaded cover images are stored outside the web root in `src\RecipeBox.Web\App_Data\recipe-images` by default (or in the configured `RecipeImages:StoragePath`). This directory is ignored by Git. Do not commit or upload database or image-storage files because they contain account and recipe data.

Local filesystem storage is intended for development and single-server experiments. Before a multi-instance or production deployment, configure persistent private storage with appropriate backup/access controls or replace `IRecipeImageStorage` with an object-storage implementation.

In Development, the app applies pending EF Core migrations at startup. Do not rely on automatic startup migrations for production deployments; apply reviewed migrations through a deliberate deployment step instead.

To stop the app, press `Ctrl+C` in its terminal.

### Restart automatically on code changes

`dotnet run` does not pick up code changes; stop it with `Ctrl+C` and start it again. To rebuild and restart automatically when you save a file, use `dotnet watch` instead:

```powershell
dotnet watch run --project src\RecipeBox.Web\RecipeBox.Web.csproj --launch-profile https
```

`dotnet watch` applies small edits in place (hot reload) and restarts the app when a change needs it. Press `Ctrl+R` in its terminal to force a restart. It does not watch the mobile app or the tunnel.

### Test the Android app against your local server

The phone cannot reach `localhost` and the app accepts only HTTPS addresses, so expose the local HTTPS server through a tunnel. Use three terminals from the repository root, and keep each one open while testing:

1. **Tunnel:** install [cloudflared](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) (`winget install Cloudflare.cloudflared`), then run `cloudflared tunnel --url https://localhost:7211 --no-tls-verify`. Copy the `https://….trycloudflare.com` address it prints. `--no-tls-verify` only skips the check of your own local development certificate between the tunnel and your PC. A quick tunnel is publicly reachable, so stop it when you are done.
2. **Web app:** in a second terminal, set `$env:Authentication__Issuer = "https://….trycloudflare.com"` to the exact tunnel URL, then run `dotnet watch run --project src\RecipeBox.Web\RecipeBox.Web.csproj --launch-profile https`. Wait until it listens on `https://localhost:7211`.
3. **App:** connect the phone with USB debugging enabled (`adb devices` must show `device`), then run `dotnet build src\RecipeBox.Mobile\RecipeBox.Mobile.csproj --configuration Debug --framework net10.0-android -t:Run`. Recipes work without signing in. To use photo capture, OCR, URL import, or cross-device sync, tap **Save to Profile**, enter the same tunnel URL, and sign in or create an account in the browser.

When to restart what:

| Change | What to do |
| --- | --- |
| Web or API code | Nothing if running with `dotnet watch`; otherwise restart the web app (keep the tunnel). |
| `cloudflared` restarted | The address changes. Restart the web app with the new `Authentication__Issuer`, then choose **Change dev server** in the app and enter the new URL. Sign in again to download profile data. |
| Mobile app code (`src\RecipeBox.Mobile`) | Rebuild and reinstall with the `-t:Run` command above. |
| New terminal for the web app | Set `Authentication__Issuer` again; environment variables do not persist between terminals. |

### Database migrations

Restore the repository's local tools once after cloning:

```powershell
dotnet tool restore
```

After changing the EF Core model, create and review a migration:

```powershell
dotnet tool run dotnet-ef migrations add DescribeTheChange --project src\RecipeBox.Web\RecipeBox.Web.csproj
```

Apply migrations to the configured database:

```powershell
dotnet tool run dotnet-ef database update --project src\RecipeBox.Web\RecipeBox.Web.csproj
```

Keep migration files in `src\RecipeBox.Web\Data\Migrations`. Never commit local database files, credentials, or production connection strings.

## Run tests

Run all tests from the repository root:

```powershell
dotnet test tests\RecipeBox.Tests\RecipeBox.Tests.csproj
dotnet test tests\RecipeBox.BrowserTests\RecipeBox.BrowserTests.csproj
```

These commands build into the projects' normal `bin` folders. On Windows, if the development web app is already running from that build output, it holds `RecipeBox.Web.exe` open and prevents the build from replacing it. The resulting `MSB3026` retry warnings followed by `MSB3027`/`MSB3021` copy errors indicate this file lock; they are not test assertion failures. Stop the app with `Ctrl+C` in its terminal (or stop debugging), then rerun the commands.

To keep the app running while testing, use the repository script instead:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test.ps1
```

The script builds and runs both test projects with a unique temporary output directory, then removes that directory and returns the test command's exit code. This avoids overwriting the executable currently in use and leaves the mobile app out of the server test run. The script requires PowerShell and the .NET SDK; it does not stop or modify the running app or its database. Use the standard commands when the app is stopped.

The solution includes real-browser tests. Install Chromium once after building the browser-test project:

```powershell
dotnet build tests\RecipeBox.BrowserTests\RecipeBox.BrowserTests.csproj
powershell -ExecutionPolicy Bypass -File tests\RecipeBox.BrowserTests\bin\Debug\net10.0\playwright.ps1 install chromium
dotnet test tests\RecipeBox.BrowserTests\RecipeBox.BrowserTests.csproj
```

GitHub Actions runs the web unit and Chromium browser tests, and compiles the Android app, on pushes and pull requests. iOS compilation is not included because it requires Apple build tooling on macOS.

Browser tests launch the web app on a temporary local port with a temporary SQLite database and remove that database afterward. They register a fresh account per journey and use deterministic OCR and recipe-import adapters only when the `RecipeBox:BrowserTestMode` setting is enabled in Development. Normal Development and Production runs use local Tesseract and the public-Internet URL importer. Never enable browser-test mode in a deployed application.

Build without running tests:

```powershell
dotnet build src\RecipeBox.Web\RecipeBox.Web.csproj
dotnet build src\RecipeBox.Mobile\RecipeBox.Mobile.csproj --framework net10.0-android
```

For local Android development, install the .NET MAUI workload with `dotnet workload install maui`, then install Android SDK Platform 36, Build-Tools 36.0.0, and JDK 17 (Android Studio's SDK Manager can install the Android packages). The CI workflow provisions these automatically. iOS builds require a Mac with Xcode; a Windows developer machine must pair with a Mac build host.

`RecipeContentTests` are unit tests for recipe text handling, section parsing, and form validation. `RecipeLayoutTests` cover default settings and reject invalid section configurations; `RecipeLayoutPagesTests` cover the live recipe preview, saved layout order/width, content preservation, and owner isolation. `LocalRecipeImageStorageTests` cover private per-owner file storage; `RecipeImagePagesTests` cover upload, replacement, removal, owner authorization, and share-link image access. `RecipeSharingTests` cover token generation, owner checks, read-only shared rendering with the saved layout, link rotation, and revocation. `RecipeUrlImporterTests` cover Schema.org parsing, bounded HTML retrieval, and public IP range checks. `RecipeImageUploadTests` cover upload signatures and size limits. `TesseractRecipeTextExtractorTests` exercise the actual local OCR engine against a small licensed fixture. `RecipePagesEndToEndTests` use ASP.NET Core's in-memory test host, fake OCR and URL-import boundaries, and an isolated temporary SQLite database to exercise HTTP page requests, photo and URL capture/review/save, authentication, and owner isolation. `RecipeBox.BrowserTests` use Playwright and Chromium against a separately launched web app to cover account registration, recipe CRUD, cover-image upload and share access, photo review/save, URL-import review/save, compact layout overview and detail expansion, drag-and-drop swapping and edge auto-scroll, and sharing in a real browser.

## Project structure

```text
RecipeBox.slnx
src/
  RecipeBox.Mobile/      .NET MAUI native client (initial shell; API integration pending)
  RecipeBox.Web/
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
  RecipeBox.Tests/    Unit tests and HTTP-level application tests
    TestData/              Licensed OCR fixture and expected text
  RecipeBox.BrowserTests/ Playwright browser journeys against a local app process
dotnet-tools.json         Pinned local development tools
```

### Where new code should go

- **Recipe persistence fields and shared recipe data rules:** `Models/`, with database configuration in `Data/ApplicationDbContext.cs`; add a migration when the schema changes.
- **Recipe capture and OCR:** `Pages/Recipes/Capture.*` for the authenticated review-before-save flow, `Services/IRecipeTextExtractor.cs` for the replaceable OCR boundary, and `Models/RecipeDraftParser.cs` for the testable English section-heading heuristic.
- **Recipe website import:** `Pages/Recipes/Import.*` for the authenticated review-before-save flow, `Services/IRecipeUrlImporter.cs` for the replaceable website boundary, `Services/RecipeUrlImporter.cs` for bounded HTML retrieval, and `Models/RecipeStructuredDataParser.cs` for Schema.org JSON-LD extraction. The importer fetches remote pages, so retain the URL, size, redirect, timeout, and public-address checks if changing its implementation.
- **Recipe layouts:** `Pages/Recipes/Layout.*` for the full-viewport per-recipe editor and live preview, `Pages/Recipes/_RecipeContent.cshtml` for editor-only section controls, `wwwroot/js/site.js` for mouse/touch drag-and-drop ordering and immediate width updates, `Models/RecipeLayoutPreference.cs` for separately persisted section order and width settings, and the `Details` page for rendering those sections. Keep layout state separate from recipe text, retain accessible move buttons, and validate every section against the recipe's available content.
- **Recipe sharing:** `Pages/Recipes/Share.*` manages a recipe owner's link; `Pages/Recipes/Shared.*` is the anonymous read-only view; `Services/RecipeShareToken.cs` generates high-entropy URL-safe tokens and hashes them before persistence. The shared page reuses `_RecipeContent.cshtml` and the owner's saved layout. Treat each share URL as a password: anyone who has it can view the recipe, and revoking or replacing the link stops access.
- **Recipe cover images:** `Services/IRecipeImageStorage.cs` defines the storage boundary; `Services/LocalRecipeImageStorage.cs` is the private local-filesystem implementation. `Pages/Recipes/Image.*` only serves an image to its recipe owner or through an active share link. Keep image data out of the database and public static files, validate content signatures and size at `Services/RecipeImageUpload.cs`, and clean up replaced/deleted images.
- **Upload constraints:** `Services/RecipeImageUpload.cs`; keep file-size and format checks at the upload boundary.
- **A recipe screen or page-specific form model:** `Pages/Recipes/`, keeping the `.cshtml` view beside its Razor Page model.
- **Shared navigation or page chrome:** `Pages/Shared/`.
- **Application-wide service registration or middleware:** `Program.cs`.
- **User-facing styles and scripts:** `wwwroot/css/` and `wwwroot/js/`.
- **Business-rule unit tests:** `tests/RecipeBox.Tests/`.
- **HTTP, persistence, and authorization behavior:** add focused tests in `RecipePagesEndToEndTests.cs` or a new test class using `RecipeAppFactory`.

Keep recipes scoped to the authenticated Identity user. Page handlers should query and update a recipe using both its ID and the current user's ID; never trust an owner ID supplied by a form.

## Scope and next increments

Recipe photos are OCR'd locally into an editable draft; the OCR source photo and unreviewed OCR output are not persisted. Website import prefers Schema.org Recipe JSON-LD, then falls back to labeled ingredient and instruction sections in visible page HTML; it also reads available title, description, and image metadata. It does not download image files or preserve the source URL as recipe metadata. This fallback cannot read content that only appears after page JavaScript runs. In both workflows, users review and can correct extracted recipe fields before saving. Separately, recipe owners can upload an optional JPEG, PNG, or WebP cover image up to 10 MB. Image bytes are stored outside the web root behind `IRecipeImageStorage`; the default local provider stores files in ignored `App_Data` and serves them only to the owner or through an active share link. This local provider is not a multi-server production-storage strategy. Users can customize the order and widths of the description, image, ingredients, and preparation sections per recipe in a full-viewport live preview with mouse/touch drag-and-drop and accessible move buttons; the overview keeps every section visible with a short summary and expands full details on demand. This changes only presentation, not saved content. Touch drags auto-scroll quickly near viewport edges. The editor adapts to phone-sized screens, including iPhone 8-sized 375x667 and 320px-wide viewports. An owner can create a revocable read-only share link; anyone with the link can view the recipe using the owner's current layout, but cannot edit the recipe. New links rotate the secret token, and the stored database value is a hash rather than the bearer token. Real-browser tests cover CRUD, cover-image upload and sharing, photo capture, URL import, compact desktop and mobile layout overview, drag-and-drop layout customization, and sharing. The next planned increment is:

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
