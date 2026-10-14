Roadmap
1. [Complete] Make the recipe basics dependable. Strengthen validation and tests around CRUD, account ownership, and failure cases. This is the foundation for everything else.
2. [Complete] Add photo capture as a review-before-save workflow. Upload a photo, extract a draft, let the user correct the title, ingredients, and steps, and save only after confirmation. Keep image storage separate from recipe data.
3. [Complete] Add recipe URL import using the same review experience. Website extraction is unreliable, so users should be able to fix the result before it becomes a recipe.
4. [Complete] Add flexible layouts after the recipe content is stable. Keep each user’s layout separate from the underlying ingredients and instructions.
5. [Complete] Add sharing after layouts. That lets us share a clear view of a recipe without making somebody else’s private recipe editable by default.

## Next: make the website installable as a PWA

The chosen direction is an installable Progressive Web App (PWA) built on the existing responsive website. This provides an app-like home-screen experience while keeping one client and the current ASP.NET Core backend.

Implement and verify the mobile experience in small steps:

* Add the web app manifest, app icons, and install metadata, and document the HTTPS requirement for installation.
* Test the install and launch experience on supported mobile browsers; preserve the accessible responsive website as the fallback.
* Improve and test one focused phone journey: sign in, browse recipes, and capture or create a recipe.
* Decide separately whether offline access is needed. Do not cache private recipe data or promise offline edits until the authentication, privacy, and synchronization behavior is designed.
* Extend GitHub Actions with mobile-browser checks where practical. A native iOS/Android app and app-store distribution are not part of this PWA phase.

Keep scalability without overbuilding
* Keep one app and one database for now. SQLite is fine locally; move to a managed relational database when deployment or usage calls for it.
* Use EF Core directly where it keeps the code simple. Avoid adding generic repositories, microservices, queues, or cloud infrastructure before a real feature needs them.
* Add replaceable seams only at external boundaries—such as OCR, image storage, and website extraction—so a provider can change without spreading provider-specific code throughout the app.
* Keep account ownership checks in every recipe query and write operation.
* Test business rules with unit tests, database and authorization behavior with integration/HTTP tests, and the key user journeys with browser tests. Don’t require every tiny detail to have an end-to-end test.
* For each feature, define what a user can do, what happens when it fails, and how we’ll test it before expanding the scope.