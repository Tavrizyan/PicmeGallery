# Deployment work handoff

Last updated: 2026-11-13 (Asia/Yerevan). Read this file before continuing from another laptop. It records work in progress, not a completed deployment. Also read [deployment.md](deployment.md), [deployment-record.md](deployment-record.md), and the owner's AGENTS.md instructions.

## Objective and decisions

- Deploy `Tavrizyan/PicmeGallery` using GitHub Actions, automatically after checks pass on pushes to `main`.
- ASP.NET Core / .NET 10 serves both UI and API. Azure App Service F1 hosts it; a Cloudflare Worker serves `photos.picme.am` and proxies to the Azure-assigned origin.
- Use one environment named `gallery`. The owner explicitly requested no `prod` prefixes/suffixes.
- Use fresh private Azure Blob storage, not the booth's existing test account. Keep Cold photo blobs and their tags/metadata indefinitely; do not configure Archive or expiry.
- Keep the existing `picme.am` root/www application and routing untouched.
- Hosting was initially approved for Frankfurt, but F1 creation failed with quota 0. The owner then explicitly approved a free F1 plan and .NET 10 Web App in Poland Central. Storage stays in Frankfurt for now.

## Verify identity before every cloud mutation

The correct Azure identity and subscription were explicitly supplied by the owner and subsequently verified:

| Setting | Value |
| --- | --- |
| Azure user | `karo.tavrizyan.96@hotmail.com` |
| Subscription name | `MainSubscription` |
| Subscription ID | `fdfffc85-91e9-45e6-8355-2e957bd82812` |
| Tenant ID | `4d5ca826-4d14-4028-b802-d6660459fb81` |

On a new laptop, authenticate privately with `az login`, then select and verify the exact subscription:

```sh
az account set --subscription fdfffc85-91e9-45e6-8355-2e957bd82812
az account show --query '{subscription:name,id:id,user:user.name}' --output json
```

Do not rely on a default subscription or a display name. Pass the explicit subscription ID to resource commands. Never read/print storage keys, connection strings, access tokens, SAS URLs, or other secrets. Enter credentials privately in the appropriate provider settings.

## Resources already created in the correct subscription

| Resource | Verified state |
| --- | --- |
| `picme-gallery-rg` | Resource group; metadata location Germany West Central |
| `picmegallerystorage` | Germany West Central; StorageV2 / Standard LRS; HTTPS only; TLS 1.2 minimum; anonymous blob access disabled |
| `photos` | Container in that account; public access `None` (private) |
| `picme-gallery-plan` | Poland Central; verified SKU `F1`, tier `Free` |
| `picme-gallery` | Linux Web App in Poland Central; HTTPS only; assigned to the plan above |
| Runtime / startup | `DOTNETCORE|10.0` / `dotnet PicmeGallery.Web.dll`; Always On false |
| Assigned origin | `https://picme-gallery.azurewebsites.net/` |

No application package has been deployed by this work. Do not treat the Web App's `Running` resource state as proof that the gallery works. No live storage, guest download, GIF, public-domain, or printed-QR checks have passed yet.

The app and storage are currently in different regions. Assess actual latency/transfer costs before deciding to change that; moving or deleting storage has not been approved.

## Wrong-subscription incident: cleanup still pending

Before the correct identity was established, resources were mistakenly created using the pre-existing CLI session:

- Wrong subscription: `82d630f7-7f2c-4538-bdaa-030c5a62bf8b`, display name `Pay As You Go`. The owner explicitly said this subscription is not theirs.
- Confirmed created there: resource group `picme-gallery-rg` and storage account `picmegalleryprod` in Germany West Central.
- A command to create private container `photos` was submitted there before the owner stopped work. Its final result was not checked.
- No App Service plan or Web App was created there by this work.
- The owner was asked about cleanup but has **not authorized deletion**. Do not touch that subscription or delete anything without explicit authorization and appropriate access. The `prod` name exists only in this mistaken account; do not reuse it.

## Local repository changes (not committed or pushed)

Branch at handoff: `main`, tracking `origin/main`. No staging, commits, pushes, merges, or PRs were performed. No Jira ticket ID has been provided; ask if required before branch/commit work under AGENTS.md.

| File | Change |
| --- | --- |
| `.github/workflows/checks.yml` | Added automatic main deployment after checks, artifact retention, Azure OIDC login, Azure deployment, Worker config generation/deployment, and origin/public liveness checks |
| `.gitignore` | Ignores generated `cloudflare/wrangler.jsonc` and `cloudflare/.wrangler/` |
| `docs/deployment.md` | Added one-time Azure/OIDC/GitHub/Cloudflare setup and recovery instructions; environment name is `gallery` |
| `docs/operations.md` | Updated automatic-deployment description |
| `docs/README.md` | Updated workflow description and links to current records |
| `docs/deployment-record.md` | New verified infrastructure record |
| `docs/work-state.md` | This handoff |
| `docs/deployment-audit.md` | Requirements audit against current main/docs and live read-only configuration checks |

Application source code and package dependencies were not changed. The existing `cloudflare/wrangler.example.jsonc` still controls Worker name `picme-gallery-proxy`, domain `photos.picme.am`, and disabled request observability. The workflow injects the origin into a generated config.

The workflow runs checks on pushes and pull requests, but only deploys pushes to `main`. It retains publish artifacts for 30 days and serializes runs per ref. Azure and Worker deployment are sequential, with no automatic rollback. Its `/health` probes check liveness only, not storage.

## GitHub authentication and environment setup

GitHub CLI is now verified as **`Tavrizyan`**, with **ADMIN** permission to `Tavrizyan/PicmeGallery` (a public repository). The earlier `ktavrizyan` READ-access blocker is resolved. On another laptop, authenticate privately as needed:

```sh
gh auth login --hostname github.com --web
```

After the owner completes private browser authentication, verify both identity and permission:

```sh
gh api user --jq .login
gh repo view Tavrizyan/PicmeGallery --json nameWithOwner,viewerPermission
```

Do not assume the Azure login also changes GitHub authentication. Authentication is local to each laptop.

### Latest resume checkpoint

On the latest resume, Azure identity/subscription were rechecked successfully and GitHub was verified as `Tavrizyan` with ADMIN access. The approved Azure identity setup and GitHub environment configuration are complete as recorded below.

Prepared `artifacts/deployment/federated-credential.json` locally (ignored/recreatable): name `picme-gallery-github`, issuer `https://token.actions.githubusercontent.com`, subject `repo:Tavrizyan/PicmeGallery:environment:gallery`, audience `api://AzureADTokenExchange`.

The owner explicitly **approved authentication and GitHub setup**: creating that Entra application/service principal, its OIDC credential, a Website Contributor assignment scoped only to the Web App, and GitHub `gallery` environment settings after admin authentication. This approval persists; do not ask again for the same setup. It excludes commit, push, and live deployment.

Azure-side setup has now been created:

| Setting | Value |
| --- | --- |
| Entra application name | `picme-gallery-github` |
| Application/client ID (`AZURE_CLIENT_ID`) | `6615cd78-70f1-4afd-a3cb-dde020c2c0d7` |
| Application object ID | `75099eec-1247-4aab-948c-e8152c1b3655` |
| Service principal object ID | `d6aaa86d-0e68-4583-afa4-1c3be054b0bc` |
| Federated credential | `picme-gallery-github`, with issuer/subject/audience above |
| Assigned role | Website Contributor |
| Assignment scope | `/subscriptions/fdfffc85-91e9-45e6-8355-2e957bd82812/resourceGroups/picme-gallery-rg/providers/Microsoft.Web/sites/picme-gallery` |

No client secret or publish profile was created or retrieved. These identifiers are not credentials.

GitHub setup was completed and verified:

- Environment: `gallery`, deployment branch policy `main` with type `branch`; no required reviewers or wait timer.
- Variables: `AZURE_WEBAPP_NAME=picme-gallery`, `AZURE_ORIGIN=https://picme-gallery.azurewebsites.net/`.
- Secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` set using the known non-secret identifiers above. Only secret-name metadata was read for verification.
- Cloudflare secret names `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` are not present in the new environment yet.
- No commit, push, live deployment, or Cloudflare route/DNS change has occurred.

The owner has now reported that Azure storage app settings (step 1) are saved. Treat this as owner confirmation, not a successful live storage test. Immediate next step: guide the owner through creating a scoped Cloudflare API token, finding the Account ID, and saving both GitHub environment secrets. Do not ask them to paste credentials into chat.

The owner subsequently requested an audit after pulling main. Local and remote `main` both matched `b6de12acd9d74632814784fbdabd28855db585e6`. See [deployment-audit.md](deployment-audit.md): infrastructure/OIDC/GitHub settings reverified; no storage management policy exists; Cloudflare secrets absent at both environment and repository scope; public hostname did not resolve; Azure `/health` timed out from this machine. The build, 27 gallery checks, 4 Worker tests, publish, workflow checks, and document file-link checks passed. No cloud changes were made during the audit; no application-code correction was required. Live acceptance and private Azure setting confirmation remain pending.

## Remaining work, in order

1. GitHub admin access is verified. On a different laptop, recheck identity/permissions before changing anything.
2. Azure OIDC application/service principal, federated credential, Web App-scoped Website Contributor assignment, and GitHub environment settings are complete; verify/reuse them. Do not create duplicates or request duplicate setup approval.
3. The GitHub environment is configured for main-only deployment, without required reviewers. Azure variables and identifier secrets are already set. Remaining environment secrets: `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_API_TOKEN`. The owner should enter these privately at https://github.com/Tavrizyan/PicmeGallery/settings/environments (select `gallery`), using a token scoped to the intended Cloudflare account/zone. Never record its value here.
4. Have the owner configure `Azure__ConnectionString` and `Azure__Container=photos` privately in Azure Web App → Settings → Environment variables → App settings, and configure the booth for the new storage account/container. Copy the connection string privately from storage account `picmegallerystorage` → Access keys; do not reveal it to the assistant. Current signer requires an HTTPS account-key connection string; a SAS-only connection is insufficient. No storage keys have been retrieved by this work.
5. Verify the intended Cloudflare account and active `picme.am` zone. The owner says the account/domain exist, but no account ID, zone, DNS records, or API permissions have been verified. Have the owner privately configure a suitably scoped Worker deployment token. Review any existing `photos.picme.am` record before authorizing a change; preserve root/www.
6. Review the full local diff, obtain approval for commit/push and the first live deployment, then publish the workflow. **A push to main will automatically deploy once credentials are configured.** Do not push prematurely. The owner has not authorized staging, committing, pushing, or a live deployment yet.
7. Follow the live rollout checklist in `docs/operations.md` using a retained booth session; verify storage contract, JPEG/GIF, guest isolation, downloads, expired-SAS renewal, and real printed QR. Record exact deployed revisions and results.
8. Address wrong-subscription cleanup only with explicit authorization, separately from this rollout.

## Verification already performed

| Command / check | Result |
| --- | --- |
| `dotnet build PicmeGallery.sln -c Release` | Passed; 0 warnings, 0 errors |
| `dotnet run --project tests/PicmeGallery.Checks -c Release --no-build` | Passed; 27 contract/access/HTTP checks |
| `node --test tests/worker-checks.mjs` | Passed; 4 tests |
| `dotnet publish src/PicmeGallery.Web -c Release -o artifacts/publish` | Passed |
| Publish artifact inspection | Expected DLL and `wwwroot/index.html` present; no known local-config filenames |
| Workflow generation dry run | Valid origin preserved intended route/private observability; invalid origins rejected |
| Ruby YAML parsing / job dependency / environment check | Passed; environment `gallery`, deploy depends on checks |
| `git diff --check` | Passed |
| Relative links in deployment record/docs index | Passed |
| Azure resource/config reads | Confirmed private storage, free F1 tier, Linux Web App, .NET 10, startup command, HTTPS-only setting |

The .NET checks project is an executable harness, run via `dotnet run`; do not imply `dotnet test` found a conventional test suite. Latest edits after the build/check run only update documentation; validate their links/formatting rather than adding implementation tests. GitHub Actions has not executed this edited workflow remotely, and no live application checks have passed.

Regional quota investigation is recorded in `deployment-record.md`. Important limitation: Azure's non-creating validation API returned Success for both blocked Frankfurt and Poland, so it did not prove quota eligibility. Poland was confirmed by the owner's approved actual F1 creation attempt and a subsequent resource read.

## Continuing on another laptop

This handoff and the implementation changes currently exist **only in this local working tree**. Copy the changed/new files listed above, or obtain approval to commit and transfer them through Git. A fresh clone alone will not contain this work until it is transferred. Build artifacts under `artifacts/` are ignored and can be recreated with the publish command. Do not transfer CLI credential caches or secrets; authenticate privately on the other laptop and verify identity again.
