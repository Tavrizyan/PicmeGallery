# Deployment documentation audit

Reviewed on 2026-11-13 against local and GitHub `main` revision `b6de12acd9d74632814784fbdabd28855db585e6`, plus the uncommitted deployment changes in this working tree. Both main revisions matched at the time of review. No cloud settings, credentials, DNS records, or deployments were changed by this audit.

**Conclusion: completed setup follows the documented architecture, but the deployment and rollout checklist are not complete.** Do not treat this as a live-service acceptance report.

## Requirements checked

| Documented requirement | Result / evidence |
| --- | --- |
| Separate gallery app; preserve legacy root/www | Pass for work performed: dedicated `picme-gallery` Web App and `picme-gallery-rg`; no legacy app or root/www changes made. Cloudflare zone routing still needs inspection before first deployment. |
| Correct Azure account/subscription | Reverified `karo.tavrizyan.96@hotmail.com` / `fdfffc85-91e9-45e6-8355-2e957bd82812`. Earlier wrong-subscription incident remains a cleanup exception below. |
| .NET 10 on a separate Free F1 plan | Pass: Linux Web App, `DOTNETCORE\|10.0`, startup `dotnet PicmeGallery.Web.dll`; plan read confirms F1 / Free in Poland Central. ARM resource read confirms that Web App references `picme-gallery-plan`. |
| Record actual OS, region, origin and resource names | Pass: [deployment record](deployment-record.md). Hosting in Poland and storage in Frankfurt are an owner-approved deviation from the initial Frankfurt hosting choice. |
| Private GPv2/LRS Blob storage, HTTPS/TLS | Pass: `picmegallerystorage`, StorageV2 / Standard LRS, HTTPS only, TLS 1.2 minimum, anonymous access disabled; `photos` container has public access `None`. |
| No expiry/deletion/Archive lifecycle policy on delivered media | Pass at account-policy level: Azure returned `ManagementPolicyNotFound`, meaning no management policy exists on this account. Actual media retention/recovery remains an operational responsibility. |
| Booth uploads explicitly Cold; final and selected originals have valid tags/metadata | Pending live booth/storage integration. The app validates Cold tier and supports JPEG/GIF finals. Account default tier is not the upload mechanism; do not change it as a substitute for verifying booth uploads. |
| Gallery has storage-read/tag-query/SAS-signing credentials | Not verified. No credentials were read. Owner must privately configure/confirm `Azure__ConnectionString` and `Azure__Container=photos`; real read/signing tests remain pending. |
| Storage networking permits booth and gallery | Account network default action is Allow. Public-network property is unset in the response. Actual booth upload and gallery read connectivity remain unverified. Private-container configuration is not proof of successful application access. |
| Azure OIDC matches GitHub environment | Pass: issuer `https://token.actions.githubusercontent.com`, subject `repo:Tavrizyan/PicmeGallery:environment:gallery`, audience `api://AzureADTokenExchange`. |
| Deployment identity has Web App-scoped permission | Pass: Website Contributor assignment scoped exactly to the gallery Web App. No client secret or publish profile was created. |
| Main-only automatic deployment | GitHub environment verified: custom branch policy `main` of type `branch`, with no reviewer/wait gate. Local workflow deploys only successful pushes to main after `checks`. Workflow is not committed/pushed yet, so edited automation is not live. |
| Required Azure GitHub settings | Pass: environment variables contain the verified Web App name/origin; secret-name metadata confirms the three Azure identifier settings. Secret values were not retrieved. |
| Required Cloudflare GitHub settings | Missing: neither `CLOUDFLARE_ACCOUNT_ID` nor `CLOUDFLARE_API_TOKEN` is present at environment or repository level. |
| Public Worker route and fixed Azure origin | Local config/workflow match: Worker `picme-gallery-proxy`, Custom Domain `photos.picme.am`, fixed assigned Azure origin, request observability disabled. Actual Cloudflare account, zone, route ownership, token permissions, caching rules, logging, and certificate are unverified. |
| Keep bearer paths out of logs; prevent shared caching | Source/config match: ASP.NET logging is Warning, safe storage-error logging, private/no-store responses, manual Worker redirect handling. Platform/zone logging and cache overrides still need review; local config cannot prove them. |
| Publish the application contents without local configuration | Pass locally: published DLL and `wwwroot/index.html` exist; known local-config filenames absent. Workflow publishes the application directory and rejects those filenames before artifact upload. No secret values were scanned or read. |
| Retain rollback artifacts/configuration | Workflow retains application artifacts for 30 days. No first deployed package/Worker revision exists from this rollout, so live rollback baseline and long-term backup/recovery owner remain pending. |
| Record deployed gallery and compatible booth revisions | Pending first deployment and compatible booth verification. |
| Live acceptance checklist | Pending: origin/public guest pages, partial/ready states, Cold/tag/metadata verification, JPEG/GIF bytes/downloads, guest isolation, expired-SAS renewal, old-QR restart test, and printed QR on another device. |

## Endpoint checks during audit

- `https://picme-gallery.azurewebsites.net/health`: request timed out from this machine after 15 seconds. No successful gallery liveness response was observed. This alone does not diagnose the cause or prove a storage fault; no application package has been deployed by our work.
- `https://photos.picme.am/health`: request failed; a separate DNS lookup could not resolve the hostname from this machine. Public DNS/Worker provisioning remains pending.

These requests used only `/health`, never a guest token or SAS URL.

## Local verification

| Command / check | Result |
| --- | --- |
| `dotnet build PicmeGallery.sln -c Release` | Passed; 0 warnings, 0 errors |
| `dotnet run --project tests/PicmeGallery.Checks -c Release --no-build` | Passed; 27 gallery contract/access/HTTP checks |
| `node --test tests/worker-checks.mjs` | Passed; 4 tests |
| `dotnet publish src/PicmeGallery.Web -c Release -o artifacts/publish` | Passed |
| Workflow YAML parsing, `gallery` environment and `checks` dependency | Passed |
| Documentation relative-file links | Passed |
| Publish layout and known local-config filename exclusion | Passed |
| `git diff --check` | Passed |

The .NET checks are an executable harness, not a conventional `dotnet test` suite. Local checks do not validate an actual Azure account's blob contract or the Cloudflare deployment. The edited GitHub Actions workflow has not run remotely yet.

## Outstanding exception and next actions

The earlier mistaken resources in subscription `82d630f7-7f2c-4538-bdaa-030c5a62bf8b` are not part of the intended environment. Their cleanup remains unapproved and was not attempted. See [work-state.md](work-state.md#wrong-subscription-incident-cleanup-still-pending).

Next: privately configure/confirm Azure storage app settings; privately add the missing Cloudflare secrets; inspect the intended zone/subdomain and platform logging/cache settings; review and authorize the first commit/push/deployment; then complete the [live rollout checklist](operations.md#rollout-checklist) and record exact deployed revisions. No application-code correction was identified as necessary by this deployment audit.
