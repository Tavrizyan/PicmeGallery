# PicmeGallery

Private photobooth downloads backed directly by Azure Blob Storage **Cold** blobs and index tags. No database, album registry, guest accounts, or booth-to-gallery API. Existing `Picme.am` is unrelated and remains untouched.

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

## Blob contract, version 1

The booth generates a permanent 32-byte random Base64URL token. `GuestKeyHash` is lowercase hex SHA-256 of the token's UTF-8 text. The raw token is not uploaded into blob names/tags.

```
sessions/{guestKeyHash}/final.jpg
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
- Owner/event links, resizing, media deletion and account administration are intentionally later phases.
