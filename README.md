# PicmeGallery

Private photobooth downloads backed directly by Azure Blob Storage **Cold** blobs and index tags. No database, guest accounts, or booth-to-gallery API. Admin-managed event links use small private Azure blob records. Existing `Picme.am` is unrelated and remains untouched.

## Project documentation

Start with the [documentation index](docs/README.md):

- [Purpose, architecture, decisions, and storage/API contract](docs/architecture.md)
- [Local setup, Azure deployment, and Cloudflare domain setup](docs/deployment.md)
- [Verification, troubleshooting, retention, and maintenance](docs/operations.md)
- [Deployment record template](docs/deployment-record.example.md) for actual resource names and rollout results

## Run locally

Install the .NET 10 SDK, then configure **server-side** environment variables (do not commit them):

```powershell
$env:Azure__ConnectionString = '<enter privately>'
$env:Azure__Container = 'photos'
dotnet run --project src/PicmeGallery.Web --urls http://localhost:5078
```

`/` shows the landing state. A real booth QR opens `/p/{accessToken}`. If storage isn't configured, the app still starts and `/health` works; guest API requests return a generic 503. No example storage credentials are included.

```powershell
dotnet run --project tests/PicmeGallery.Checks
node --test tests/worker-checks.mjs
dotnet publish src/PicmeGallery.Web -c Release -o artifacts/publish
```

Checks use a fake store for HTTP access isolation, waiting/partial/ready states, old non-expiring tokens, private headers, and error sanitization. They require no cloud account. A real storage test is still needed for Cold/index-tag permissions and account support.

## Gallery design and phone saving

The gallery reuses the logos, favicons, Apple touch icon, background photography, purple colors (`#251b62` / `#a84da7`), and Lato typeface from the `web.picme.am` website. Branding assets and fonts are served locally from `wwwroot/brand`; the main Picme website is unchanged.

Tap a finished print or original to open its preview. Use the previous/next buttons, arrow keys, or a horizontal swipe to browse; Escape or the close button returns focus to the photo. Animated GIF finals remain animated.

On iPhones/iPads, **Save to phone** opens the preview and prepares the original file; **Save to Photos** then opens the native share sheet, where the guest chooses **Save Image**. Android guests can use **Save / share** with a compatible Photos/Gallery app, or download the original and find it in Downloads/the gallery’s Downloads album. The page also explains long-press saving. Availability and wording depend on the browser and installed apps. A website cannot silently write to the phone’s photo library.

File sharing requires HTTPS (localhost works for development) and browser support for `navigator.canShare({ files })`. It uses an authenticated, uncached same-origin stream at `/api/media/{token}/{id}?file=true`, preserving the original bytes and stored content type without requiring Azure CORS. Existing preview/download redirects remain available. Only the current preview is prepared, requests are aborted when changing/closing the preview, and files over 50 MiB use the download/long-press fallback to limit phone memory usage. Cancellation is silent; failed/unsupported sharing keeps download and long-press saving available.

With the local app on port 5078, `tests/gallery-browser-checks.js` can be run through the Playwright `browser_run_code` tool’s `filename` input. It mocks private API/media responses and native sharing, checking responsive layouts, previews, keyboard navigation, focus restoration, sorting, refresh, file activation, cancellation, failures, GIFs, and waiting/partial/retry states. It writes temporary screenshots under `/tmp`. Before rollout, verify **Save Image** on an actual iPhone in Safari and the download/share choices on an actual Android phone over HTTPS, including GIFs and in-app QR browsers.

## Admin and event-owner galleries

Open `/admin` to manage event links. Set **server-side Azure App Service application settings** before signing in:

| Setting | Value |
| --- | --- |
| `Admin__Username` | Your administrator username. No default. |
| `Admin__Password` | Your administrator password. No default. |
| `Admin__DataProtectionKeysPath` | A persistent, private key-ring directory; use `/home/data/picme-gallery-keys` on Linux App Service with persistent App Service storage enabled. Keep this directory across deployments. |

The login form uses HTTPS, an eight-hour secure HttpOnly session cookie, CSRF protection, and a limit of ten login attempts per minute per server-observed client IP. Missing credentials disable login; changing either configured credential invalidates existing admin sessions. No credentials are shipped to the browser. Behind Cloudflare, several users may share a server-observed IP and the login-attempt budget.

Create an event using the **exact Event ID configured in the booth** and a display name, then copy its owner link. Existing uploaded photos with that Event ID are included; no booth changes or re-upload are required. Use a distinct Event ID for each separate event. The admin page lists only explicitly created owner links, ordered by their creation date newest first across all pages. It reads the small management records under `gallery/events/`; it does not scan photo blobs or maintain an automatically discovered event index. A guest’s QR gallery displays its **Event ID** above the photos as soon as the final is available. Copy that ID into the admin form to create an owner link for the corresponding event. The ID comes from the already-loaded final’s validated tags, without additional discovery/storage requests. An Event ID is a non-secret label and does not grant event access. The event list supports copying, opening, replacing, and revoking links. Replacement/revocation requires confirmation and uses optimistic concurrency to avoid overwriting another admin tab’s change.

The organizer opens `/e/{ownerToken}` without signing in. The grid displays **finals only**. Preview opens the finished print or animated GIF; **Final** downloads it; **All photos** opens the session’s final and originals using the existing phone-save viewer. **Download all photos (.zip)** streams every available final and original across all pages, grouped into session folders. It does not load the complete archive into server memory or create a stored ZIP. Two archive requests may run concurrently per app instance; large events remain subject to Azure/Cloudflare request duration, bandwidth, and F1 quotas. Interrupted archives must be downloaded again. Photos still uploading are included only when available at the time each session is read.

Finals are discovered via the `EventId` / `Kind=Final` Azure tag index in pages of 24. This index is eventually consistent: recently uploaded finals may appear after a delay. The page loads one page initially and offers **Load more moments**; while the tab is visible it scans all event pages for new sessions every minute. Manual refresh also scans all pages. Session counts describe loaded moments, not a separately maintained total. There is no resized-thumbnail service; the browser lazily loads original final images.

Event access records live in the existing private container under `gallery/events/{eventId}.json`, with hashed-token lookups under `gallery/access/{tokenHash}.json`. They use **Hot** tier for small mutable records; photo blobs remain Cold. The gallery’s Azure credential now also needs permission to read/write these records. The stored owner token is encrypted with ASP.NET Core Data Protection so an authenticated administrator can copy the link again; restoring that capability requires preserving the key ring. If keys are lost, existing owner tokens still validate by hash, but the administrator must replace a link to copy a new one. Old lookup blobs can remain after replacement/revocation; the current event record always decides access.

Owner links grant read access to one event’s photos. Replacing or revoking a link rejects subsequent gallery, session, media, and ZIP requests; already downloaded files cannot be recalled, in-progress requests may finish, and previously issued media SAS URLs can remain valid for up to five minutes. Guest print links remain independent.

For local admin development, use HTTPS:

```text
dotnet run --project src/PicmeGallery.Web --no-launch-profile --urls "https://localhost:5079;http://localhost:5078"
```

Supply the admin/storage settings privately through your environment or ignored `appsettings.Local.json`; do not commit real credentials. `dotnet run --project tests/PicmeGallery.Checks` includes admin login/CSRF/rate limiting, scoped event access, replace/revoke, and ZIP-content checks with fake storage. `tests/event-browser-checks.js` runs through the Playwright browser tool’s `filename` input against port 5079, with mocked admin/event APIs. It covers login/logout, event management, responsive owner/admin pages, final-only pagination, original-session navigation, and revoked links. A real Azure read/write/tag-index and HTTPS deployment check remains necessary.

## Blob contract, version 1

The booth generates a permanent 32-byte random Base64URL token. `GuestKeyHash` is lowercase hex SHA-256 of the token's UTF-8 text. The raw token is not uploaded into blob names/tags.

```
sessions/{guestKeyHash}/final.jpg (or final.gif for an animated session)
sessions/{guestKeyHash}/originals/001.jpg
sessions/{guestKeyHash}/originals/002.png
```

Final and originals have `EventId`, `GuestKeyHash`, `Kind` index tags. Kind is `Final` or `Original`. EventId matches `^[a-z0-9][a-z0-9_-]{0,63}$` and is configured manually in the booth.

Metadata keys are lowercase: `schemaversion=1`, `contentsha256`, `capturetimeutc`, `originalindex` (`0` for final), `expectedoriginalcount` (1–100). Actual bytes/content type are preserved; no resizing pipeline is included. Every blob must be private and explicitly Cold.

The gallery queries `GuestKeyHash` tags. It also reads the deterministic final and uses prefix fallback for originals while Azure's asynchronous tag index catches up. It verifies every returned blob's tags/metadata, type and tier. Source blobs and metadata must be retained indefinitely; do not configure Archive or expiry for them.

The UI calls `/api/guest/{token}` and `/api/media/{token}/{id}`. Downloads receive fresh five-minute, read-only object SAS URLs. The guest token itself has no expiry. Unknown well-formed tokens have a generic waiting response; no guest can list another session.

## Azure App Service F1

Create a separate F1 Web App with the .NET 10 runtime and deploy the publish output. Configure `Azure__ConnectionString` and `Azure__Container` in App Service settings, using credentials capable of listing/filtering tags, reading properties/tags, and signing individual blob reads. No secrets go in client assets.

F1 is an initial development/testing target with hard quotas; it is not an always-on availability guarantee. The Azure-assigned HTTPS hostname is the origin. Test the deployed `/health`, guest route, tag query, and actual downloads before issuing production prints. No Azure resources are provisioned by this repository.

For public `photos.picme.am`, deploy the small reverse proxy in `cloudflare/` on that subdomain only. A plain DNS CNAME does not replace Azure Host routing. Cloudflare terminates the public certificate and contacts the F1 origin with its Azure hostname. This preserves permanent QR URLs when the origin changes. Keep the existing root/www application unchanged.

Azure Functions was discussed as an alternative, but this initial implementation retains the last selected F1 Web App baseline. The storage contract permits a future stateless Functions backend without booth changes.

## Deployment and retention notes

- Keep token-bearing paths out of Cloudflare/application logs. ASP.NET request information logs are disabled in the supplied configuration.
- Do not cache guest APIs, private pages, or SAS redirects at a shared edge. Static CSS/JS can be hosted normally.
- Preserve the hostname/route and blob key/tag contract across deployments; there is no mapping database to migrate.
- Resizing, media deletion, event deletion and multiple administrator accounts are intentionally later phases.

The final may be an animated GIF (`image/gif`, `final.gif`). The same permanent `/p/{accessToken}` route displays and downloads it without converting its bytes. Finals are looked up by their deterministic JPEG/GIF name while original access checks remain unchanged. Deploy this gallery update with deferred GIF uploads in PicmeBooth.
