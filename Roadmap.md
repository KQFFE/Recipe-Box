Roadmap
1. [Complete] Make the recipe basics dependable. Strengthen validation and tests around CRUD, account ownership, and failure cases. This is the foundation for everything else.
2. [Complete] Add photo capture as a review-before-save workflow. Upload a photo, extract a draft, let the user correct the title, ingredients, and steps, and save only after confirmation. Keep image storage separate from recipe data.
3. [Complete] Add recipe URL import using the same review experience. Website extraction is unreliable, so users should be able to fix the result before it becomes a recipe.
4. [Complete] Add flexible layouts after the recipe content is stable. Keep each user’s layout separate from the underlying ingredients and instructions.
5. [Complete] Add sharing after layouts. That lets us share a clear view of a recipe without making somebody else’s private recipe editable by default.

## Next: build native Android and iOS apps

The goal is apps that install and run on Android and iOS devices. A PWA is not a required middle step; keep the responsive website as a useful web experience, but do not invest in PWA packaging unless it provides a separate user benefit.

The shared client framework is .NET MAUI. The app in `src\RecipeBox.Mobile` now supports system-browser sign-in, paged recipe browsing, and recipe creation against the API. Device testing and production link association remain outstanding; setup and sign-in behavior are documented in the README.

Next:

* [Complete] Implement the OpenID Connect issuer with ASP.NET Core Identity and OAuth authorization-code sign-in with PKCE for the native public client. Keep passwords in the system-browser Identity flow; do not embed a client secret in the app.
* [Complete] Implement the versioned bearer-token recipe API with owner-scoped DTO endpoints and tests for authentication, validation, and cross-account isolation.
* [Implemented] Add mobile sign-in and the browse/create recipe journey against the API. Add owner-authorized image endpoints before supporting private image display or uploads.
* Add owner-authorized image endpoints, then expose photo capture and recipe URL import through authenticated operations.
* Decide whether offline recipe access or edits are required. Do not cache private recipe data or promise offline edits until privacy and synchronization behavior are designed.
* Build and test one focused native journey on real Android and iOS devices: sign in, browse recipes, and create a recipe or capture a photo.
* GitHub Actions now compiles the Android target. Add iOS builds and signing when Apple signing credentials and macOS build infrastructure are available.

Keep scalability without overbuilding
* Keep one app and one database for now. SQLite is fine locally; move to a managed relational database when deployment or usage calls for it.
* Use EF Core directly where it keeps the code simple. Avoid adding generic repositories, microservices, queues, or cloud infrastructure before a real feature needs them.
* Add replaceable seams only at external boundaries—such as OCR, image storage, and website extraction—so a provider can change without spreading provider-specific code throughout the app.
* Keep account ownership checks in every recipe query and write operation.
* Test business rules with unit tests, database and authorization behavior with integration/HTTP tests, and the key user journeys with browser tests. Don’t require every tiny detail to have an end-to-end test.
* For each feature, define what a user can do, what happens when it fails, and how we’ll test it before expanding the scope.