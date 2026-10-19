Roadmap
1. [Complete] Make the recipe basics dependable. Strengthen validation and tests around CRUD, account ownership, and failure cases. This is the foundation for everything else.
2. [Complete] Add photo capture as a review-before-save workflow. Upload a photo, extract a draft, let the user correct the title, ingredients, and steps, and save only after confirmation. Keep image storage separate from recipe data.
3. [Complete] Add recipe URL import using the same review experience. Website extraction is unreliable, so users should be able to fix the result before it becomes a recipe.
4. [Complete] Add flexible layouts after the recipe content is stable. Keep each user’s layout separate from the underlying ingredients and instructions.
5. [Complete] Add sharing after layouts. That lets us share a clear view of a recipe without making somebody else’s private recipe editable by default.

## Next: build native Android and iOS apps

The goal is apps that install and run on Android and iOS devices. A PWA is not a required middle step; keep the responsive website as a useful web experience, but do not invest in PWA packaging unless it provides a separate user benefit.

The shared client framework is .NET MAUI. The app in `src\RecipeBox.Mobile` is a native, offline-first mirror of the website: a Home page, My recipes card list, recipe details, create/edit form with cover images, Capture and Import pages, and an Account tab, all styled with the website's Bootstrap look. Everything works signed out and offline except photo OCR and URL import; an account is only needed to save recipes to a profile for other devices. The complete earlier native journey has been tested on a real Android device; the redesigned UI is pending device testing. iOS device testing and production link association remain outstanding; setup and sign-in behavior are documented in the README.

Mobile parity with the web app (the native app must look and function the same offline and online):

* [Implemented] Phase 1: tabbed shell, Home, My recipes, recipe details, create/edit/delete, cover images, Capture, Import, and Account (sign in, sync, sign out) with website styling.
* [x] Phase 2 (implemented, device test pending): customize recipe layout (stored locally, synced to the profile through optional `sectionOrder`/`fullWidthSections` API fields) and share links (`GET/POST/DELETE /api/v1/recipes/{id}/share`; needs an account and network). Visual polish from device testing remains open.

Next:

* [Complete] Implement the OpenID Connect issuer with ASP.NET Core Identity and OAuth authorization-code sign-in with PKCE and rotating refresh tokens for the native public client. Keep passwords in the system-browser Identity flow; do not embed a client secret in the app.
* [Complete] Implement the versioned bearer-token recipe API with owner-scoped DTO endpoints and tests for authentication, validation, and cross-account isolation.
* [Implemented] Add mobile sign-in and the browse/create/edit/delete recipe journey against the API.
* [Implemented] Add owner-authorized image endpoints, photo capture/OCR drafts, and recipe URL import drafts through authenticated operations.
* [Implemented] Add mobile cover-image viewing, replacement, and removal for recipes.
* [Implemented] Web guest mode: signed-out visitors can create, import, and capture recipes; they are kept under a hidden guest profile and merge into the account on login or registration. [ ] Clean up stale guest profiles. [ ] On-device photo OCR for mobile.
* [Implemented] Make the mobile app local-first: signed-out users can create, browse, edit, and delete recipes offline without configuring a server. Keep local recipes and images in app-private storage. URL import runs on the device; photo/OCR capture still requires an account and network access.
* [Implemented] Add an Account tab (like the website's Login/Register). Signing in or creating an account promotes local recipes to the account and syncs account recipes and cover images to the device.
* [Implemented] Sync local edits and profile data with last-edit-wins conflict resolution based on UTC modification timestamps. Remove profile-bound recipes on sign-out while preserving device-only recipes.
* [ ] Build and test offline and profile-sync journeys on Android, including first-time promotion, reconnection, and edits made on another device.
* [ ] Deploy a stable HTTPS API/issuer and configure `RecipeBoxServerUrl` for release builds; temporary development tunnels are not production endpoints.
* [ ] Build and test the complete native journey on a real iOS device when Mac/Xcode infrastructure is available.
* GitHub Actions now compiles the Android target. Add iOS builds and signing when Apple signing credentials and macOS build infrastructure are available.

Keep scalability without overbuilding
* Keep one app and one database for now. SQLite is fine locally; move to a managed relational database when deployment or usage calls for it.
* Use EF Core directly where it keeps the code simple. Avoid adding generic repositories, microservices, queues, or cloud infrastructure before a real feature needs them.
* Add replaceable seams only at external boundaries—such as OCR, image storage, and website extraction—so a provider can change without spreading provider-specific code throughout the app.
* Keep account ownership checks in every recipe query and write operation.
* Test business rules with unit tests, database and authorization behavior with integration/HTTP tests, and the key user journeys with browser tests. Don’t require every tiny detail to have an end-to-end test.
* For each feature, define what a user can do, what happens when it fails, and how we’ll test it before expanding the scope.