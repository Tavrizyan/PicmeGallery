# Operations and maintenance

## Rollout checklist

Use synthetic test photos and an actual retained/uploaded booth session. Keep its token in an appropriate private test record, not a public issue, deployment log, or this documentation.

- [ ] Confirm the deployed revision, .NET runtime, account/container, origin, and public hostname in the deployment record.
- [ ] Check `/health`, then the real guest page through the Azure origin and public hostname. Liveness alone is insufficient.
- [ ] In Azure, verify the final and each selected original are Cold, private, and carry the [version 1 tags/metadata](architecture.md#storage-contract-version-1).
- [ ] Open the link while only the final is available: the final should display and originals should show as pending. Finish the queue and refresh to reach `ready`.
- [ ] Download the final and every selected original. Check that files match the retained source bytes and that discarded retakes are absent.
- [ ] When using the GIF integration, check animation and the downloaded GIF, plus the original JPEG/PNG files. Deploy compatible booth/gallery revisions.
- [ ] Verify a second session/token cannot read the first session's assets and direct unsigned access to the container fails.
- [ ] Wait beyond a download SAS's five-minute lifetime, then reload the permanent QR page and obtain a fresh working download.
- [ ] Redeploy/restart the gallery and reopen an older test QR. There is no database state to recreate.
- [ ] Scan a printed QR on another device through the public hostname. Confirm the link is not a localhost address.

Automated checks in `tests/PicmeGallery.Checks` cover the contract, token isolation, waiting/partial/ready HTTP states, safe errors, and SAS signing. `tests/worker-checks.mjs` covers fixed-origin proxying and redirect behavior. They do not prove a particular Azure account's permissions, real printed QR, or deployed domain works.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| `/health` works but guests get 503 | The health route does not access Azure. Check server-side `Azure__ConnectionString`, `Azure__Container`, HTTPS endpoints, network reachability, permissions, and the storage account/container itself. |
| Page stays in waiting | Check whether that retained booth session was actually requested for upload. Confirm its final succeeded, then check the exact hash/prefix, filename, Cold tier, MIME type, tags, and schema. A well-formed unknown token also waits by design. |
| Final displays but originals remain incomplete | Check pending/failed booth jobs, expected count, matching indices/filenames, event/hash tags, and metadata. Tag indexing can lag; the gallery also tries the session's prefix listing. |
| Metadata loads, but downloads return 503 | The server must be able to sign a fresh individual SAS. A connection that only contains a SAS is insufficient for the current signer. Check account-key configuration and the app's storage-read permissions/network. |
| A copied blob URL stops working | Blob SAS URLs expire. Reopen the permanent `/p/{token}` page and request a new preview/download. Never print the short-lived blob SAS itself. |
| JPEG works but an animated final is missing | Confirm the deployed gallery revision includes `final.gif` resolution, the final has `image/gif`, and the matching booth integration uploaded it under the same contract. |
| Azure origin works, public domain fails | Check the Cloudflare account/route, certificate/DNS conflict, and exact `AZURE_ORIGIN`. It must be an HTTPS `azurewebsites.net` origin without an extra path/query. |
| Public page shows the Azure default page or Host errors | Confirm deployment targeted the gallery Web App, ZIP layout is correct, and the Worker forwards to that assigned origin rather than a plain CNAME/public Host binding. |
| Images briefly fail or page becomes unavailable | Check App Service/Worker quotas, platform health, account/network changes, and Cold read costs. Use the page's manual refresh after the underlying issue is resolved. |
| An old print no longer opens | Check domain/routing continuity, storage retention/lifecycle rules, moved/deleted blobs, and credentials before changing any printed-link contract. |

Inspect errors without publishing the token, connection string, account key, or full SAS query. The app deliberately logs only the exception type for storage failures; use Azure-side diagnostics and controlled configuration checks for further investigation.

## Preserve printed links

The token itself never expires, but availability is an operational responsibility:

1. Retain the public hostname and `/p/{token}` route. When moving hosts, update the proxy origin while preserving the public URL.
2. Retain original blob paths, contents, tags, and metadata. Do not apply deletion/expiry/Archive rules to the delivered media.
3. Keep Azure credentials current and both upload/read paths working. Do not make the container public as a workaround for a failing signer.
4. Maintain recoverable copies of media/metadata and non-secret deployment configuration. Verify that restored assets still resolve from an old test token.

The gallery cannot recover a token from its hash. If support needs an existing guest link, use the guest's printed QR or the booth's retained protected session record. There is no gallery-side token directory to search or recreate.

## Logs, caching, and access scope

Guest links are bearer credentials. Avoid recording full token paths in application, proxy, analytics, or platform request logs. The supplied ASP.NET configuration suppresses informational request logging, and the Worker example disables its request observability. Review any platform/zone logging added outside the repository.

The app and Worker return private/no-store responses and `Referrer-Policy: no-referrer`; the app also adds no-index and content-security headers. These reduce incidental exposure and shared caching but do not replace the guest-token checks or private storage permissions. Do not install an edge cache rule that serves one guest's API/media redirect to another.

An Event ID is a grouping label, not a password. Event owner/admin access will need its own implemented authorization design; it is not supplied by knowing an event label today.

## Credential rotation

Use Azure's supported key-rotation process, maintaining a usable credential while updating PicmeGallery's app settings and the booth's protected settings. Do not store rotated keys in Git or a deployment record. Verify a real guest download and a future upload after the change. The guest token/hash does not depend on the account key, so printed links need not change when credentials rotate correctly.

## Upgrades, rollback, and compatibility

- Work on `main` unless the owner asks for a separate branch. Review pending local changes before committing or publishing.
- Run the gallery/proxy checks and `dotnet publish` after application changes. For documentation-only updates, validate document links and commands against the repository rather than inventing new implementation tests.
- Deploy the UI and API together. If changing media formats, schema, token/hash computation, filenames, or tag semantics, update the booth and gallery compatibly. Preserve reads for already printed sessions.
- Keep the previous published application package and Worker configuration/revision for rollback. Restore the origin/configuration or redeploy the compatible package; do not delete media to undo a code rollout.
- The current CI builds/checks/publishes output only. Automatic deployment, infrastructure as code, owner galleries, image optimization, and cleanup are separate future changes.

## Deployment inventory

Copy [deployment-record.example.md](deployment-record.example.md) into an appropriate environment record after verifying the actual rollout. Record resource names and revision IDs, not secrets or guest URLs. Review that record whenever the origin, storage destination, plan, domain, or supported media contract changes.
