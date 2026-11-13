# Configure and deploy PicmeGallery

This is an operator guide, not a statement that resources have already been created. Use a separate gallery Web App and the `photos.picme.am` subdomain. Do not deploy into the legacy `Picme.am` application or change its root/www routing.

## 1. Prerequisites

- A reviewed checkout of `main`, .NET 10 SDK, and Node.js for the proxy checks/Cloudflare tooling. The SDK selection is in [global.json](../global.json).
- Access to an Azure subscription and a real storage account/container usable by both PicmeBooth and PicmeGallery.
- Permission to create/configure a separate Azure App Service Web App, with the .NET 10 runtime and Free F1 where available.
- For the proposed public hostname: access to the Cloudflare account managing `picme.am`, Workers, and that subdomain.
- For the command-line deployment examples: [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) and [Wrangler](https://developers.cloudflare.com/workers/wrangler/).

F1 is a development/testing starting point with resource quotas, not an always-on service guarantee. Check the current plan/runtime availability for the selected OS and region in Azure. Microsoft's [ASP.NET Core quickstart](https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore) documents the Web App setup.

## GitHub Actions deployment

The [workflow](../.github/workflows/checks.yml) runs gallery and proxy checks and publishes the application on pushes and pull requests. A successful **push to `main`** also retains the publish artifact for 30 days, deploys that same artifact to Azure, checks origin liveness, deploys the Cloudflare Worker, and checks public liveness. Pull requests and other branches do not deploy. Runs for the same ref are serialized so deployments cannot overlap.

### One-time resource setup

1. Prepare the private storage account/container as described in section 2. Source media must remain private, online, and retained.
2. Create a **separate** Azure Web App using Code, .NET 10, and an F1 plan where supported. Choose and record its OS and region. For Linux, set the startup command to `dotnet PicmeGallery.Web.dll` if needed. Keep HTTPS enabled. Configure the two storage app settings from section 3 privately in Azure; GitHub does not need the storage connection string. Do not enable an additional generated deployment workflow in Deployment Center.
3. Create a GitHub environment named **`gallery`** under repository Settings → Environments. Restrict deployment branches to `main`. Leave required reviewers disabled for automatic releases. If this repository's GitHub plan does not support the environment/protection settings, resolve that before configuring federation; the subject below depends on the environment name.
4. Register a Microsoft Entra application for GitHub deployment. Assign its service principal **Website Contributor** scoped to this Web App, rather than the subscription. Add a GitHub Actions federated credential for repository `Tavrizyan/PicmeGallery`, entity type **Environment**, name **`gallery`**:

   ```text
   Issuer:   https://token.actions.githubusercontent.com
   Subject:  repo:Tavrizyan/PicmeGallery:environment:gallery
   Audience: api://AzureADTokenExchange
   ```

   Use the application/client ID, tenant ID, and subscription ID for the GitHub settings below. No client secret or publish profile is needed. See Microsoft's [OIDC deployment instructions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions). A branch-based federated subject will not match this environment-based job.
5. Ensure the `picme.am` zone is active in the intended Cloudflare account and available for a Worker Custom Domain on `photos.picme.am`. Review any existing record for that subdomain before changing it; preserve root/www routing. Create a Cloudflare API token using **Edit Cloudflare Workers**, restricted to the intended account and zone, with permission to deploy the Worker and manage its Custom Domain. See [Cloudflare's GitHub Actions guide](https://developers.cloudflare.com/workers/ci-cd/external-cicd/github-actions/) and [Workers permissions](https://developers.cloudflare.com/workers/authorization/workers/).
6. Populate the `gallery` environment settings below **before pushing the workflow to `main`**. The first successful main run will publish the Worker and attach its Custom Domain; confirm that rollout is authorized before pushing.

### GitHub environment settings

Under Settings → Environments → `gallery`, add:

| Kind | Name | Value |
| --- | --- | --- |
| Variable | `AZURE_WEBAPP_NAME` | Exact gallery Web App resource name. |
| Variable | `AZURE_ORIGIN` | Assigned origin, e.g. `https://<assigned-host>.azurewebsites.net/`; copy it from Azure rather than constructing it from the app name. |
| Secret | `AZURE_CLIENT_ID` | Entra application/client ID. |
| Secret | `AZURE_TENANT_ID` | Entra directory/tenant ID. |
| Secret | `AZURE_SUBSCRIPTION_ID` | Subscription containing the Web App. |
| Secret | `CLOUDFLARE_ACCOUNT_ID` | Account containing the `picme.am` zone. |
| Secret | `CLOUDFLARE_API_TOKEN` | Scoped deployment token; enter privately in GitHub. |

The workflow generates `cloudflare/wrangler.jsonc` from the checked-in example and injects `AZURE_ORIGIN`. The example controls the Worker name, public domain, compatibility date, and disabled request observability. Generated local config and Wrangler state are Git-ignored. Azure storage credentials belong only in the Web App's app settings and the booth's protected settings.

### First rollout and recovery

After reviewing the workflow and setting up resources/authentication, push the approved revision to `main`. Open GitHub Actions → **Gallery checks and deployment** and inspect both jobs. Re-run a failed deployment only after resolving its cause; rebuilding is not required to re-run the failed deploy job while its artifact remains available.

The two liveness probes verify the gallery response, but do not access storage. Complete section 7 and the [live rollout checklist](operations.md#rollout-checklist) with a retained booth session before issuing prints. First-time DNS/certificate provisioning may outlast the public probe's retries; verify provisioning and rerun the failed job if necessary.

Azure and Cloudflare deployment are sequential, not atomic. If the Worker deployment fails, Azure may already be updated. There is no automatic rollback. Keep the previous successful run/artifact and Worker revision for recovery; redeploy a reviewed compatible package and Worker configuration when needed. Retain a longer-lived copy of a known-good artifact if recovery must extend beyond GitHub's 30-day retention. Record resource names, deployed commit, Worker revision, and live check results using the [deployment record](deployment-record.example.md).

The remaining sections also document local verification and manual deployment as an operator fallback.

## 2. Prepare Azure Blob Storage

1. Create or select a standard general-purpose v2 storage account that supports Cold block blobs and blob index tags. A standard LRS account is one possible initial configuration; choose the redundancy and region for the actual retention requirements.
2. Create a dedicated container, for example `photos`, with anonymous access set to **Private**. The applications do not create the cloud account/container for you.
3. Obtain the HTTPS account connection string from the storage account's access-key configuration. Keep the real value in the booth's protected settings and Azure app settings, not in source control or this documentation.
4. Confirm that the account's networking permits the booth to upload and the Web App to read it. Private container access and network reachability are separate settings.
5. Avoid expiry, deletion, or Archive lifecycle rules on delivered media. The booth explicitly selects Cold at upload, so changing the account's default tier is not the upload mechanism.

The current gallery uses an account-key connection string to read properties, tags, and lists, filter by tags, and sign individual blob SAS URLs. A connection containing only a pre-generated SAS may read storage but cannot satisfy `CanGenerateSasUri` in this implementation. Managed identity/user-delegation signing is future implementation work. The booth additionally needs upload/tag-write/container-read permissions.

Reference: [Azure connection strings](https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string), [blob index tags](https://learn.microsoft.com/en-us/azure/storage/blobs/storage-manage-find-blobs), and [access tiers](https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview).

## 3. Run locally against cloud storage

From the PicmeGallery repository root in PowerShell:

```powershell
$env:Azure__ConnectionString = [System.Net.NetworkCredential]::new('', (Read-Host 'Azure storage connection string' -AsSecureString)).Password
$env:Azure__Container = 'photos'
dotnet run --no-launch-profile --project src/PicmeGallery.Web --urls http://127.0.0.1:5078
```

The runtime reads these variables as `Azure:ConnectionString` and `Azure:Container`. The secret input is masked rather than embedded in the command history. Close the shell or clear its environment variables when finished.

Open `http://127.0.0.1:5078/health` and `/`. To test uploaded assets, use `/p/{an-actual-test-session-token}` from the corresponding booth session. A newly invented token will only show waiting.

The app can start without Azure settings; that is useful for checking the landing page, but guest API calls then return 503. `/health` alone does not prove storage works.

Configuration reference:

| Name | Where | Required value |
| --- | --- | --- |
| `Azure__ConnectionString` | Local environment or Azure Web App app setting | HTTPS Azure Blob connection string capable of reads/tag queries and SAS signing. |
| `Azure__Container` | Same | The existing private container name, for example `photos`. |
| `AZURE_ORIGIN` | Cloudflare Worker variable | The gallery Web App's assigned HTTPS `azurewebsites.net` origin, with `/` as its path. No guest token or storage key. |
| Guest URL pattern | PicmeBooth Uploads settings | The verified public route, normally `https://photos.picme.am/p/{accessToken}`. This is not a gallery environment variable. |

The optional `appsettings.Local.json` is ignored by Git, but environment variables are the simplest local setup. If you use a local settings file, check that it is absent from the publish output and ZIP: being Git-ignored does not itself prevent publishing a JSON file.

Local emulator connection strings, including `UseDevelopmentStorage=true`, are intentionally unsupported. Do not rewrite one to `https` and expect it to become a cloud account.

## 4. Validate and publish

```powershell
dotnet run --project tests/PicmeGallery.Checks -c Release
node --test tests/worker-checks.mjs
dotnet publish src/PicmeGallery.Web -c Release -o artifacts/publish
```

The automated checks use synthetic/fake storage and test real SAS signing without contacting an Azure account. They complement the live rollout checks below. GIF support must be included in the reviewed published revision when the booth produces GIF finals.

Inspect `artifacts/publish` for local secrets/configuration files, then package the **contents** of the publish directory:

```powershell
Compress-Archive -Path artifacts/publish/* -DestinationPath artifacts/PicmeGallery.zip -Force
```

The ZIP root must contain `PicmeGallery.Web.dll`, its dependencies, configuration, and `wwwroot`. Do not ZIP the source repository or nest everything under an extra `publish` directory. See [App Service ZIP deployment](https://learn.microsoft.com/en-us/azure/app-service/deploy-zip).

## 5. Configure and deploy the Azure Web App

1. In Azure, create a separate Web App publishing **Code**, select the .NET 10 runtime, and select/create its own App Service plan on F1. Record the OS, region, app name, resource group, and assigned HTTPS hostname.
2. In the Web App's environment variables/app settings, add `Azure__ConnectionString` and `Azure__Container`. Use the app-settings section with these exact names. Save/apply and allow the restart.
3. Deploy the publish ZIP, replacing placeholders below with the new gallery app's values:

```powershell
az login
az account set --subscription '<subscription-id-or-name>'
az webapp deploy --resource-group '<gallery-resource-group>' --name '<gallery-web-app-name>' --src-path artifacts/PicmeGallery.zip --type zip
```

For a Linux app that needs an explicit startup command, use `dotnet PicmeGallery.Web.dll`. For Windows, use the published ASP.NET Core hosting configuration. Confirm that the app's configured runtime matches the published `net10.0` framework-dependent output.

4. Visit `https://<assigned-host>.azurewebsites.net/health`, then test a real uploaded session at `/p/{token}` on that origin. Verify both previews and downloads, not just the landing page.
5. Record the deployed commit and successful checks in a [deployment record](deployment-record.example.md).

Azure app settings are supplied as environment variables and changing them restarts the app. Follow Microsoft's [configuration guide](https://learn.microsoft.com/en-us/azure/app-service/configure-common) for the current portal controls. With GitHub Actions configured above, the workflow deploys the checked publish output automatically; the CLI command is a manual fallback.

## 6. Configure `photos.picme.am` through Cloudflare

F1 does not support direct custom-domain binding. The selected approach leaves Azure using its assigned HTTPS hostname and puts the included Worker on the public gallery subdomain. A plain CNAME does not perform the HTTP Host routing needed by this design. See [Azure custom-domain requirements](https://learn.microsoft.com/en-us/azure/app-service/app-service-web-tutorial-custom-domain).

From the repository root:

```powershell
Set-Location cloudflare
Copy-Item wrangler.example.jsonc wrangler.jsonc
```

Edit `wrangler.jsonc`:

- Set `vars.AZURE_ORIGIN` to the actual assigned Azure HTTPS hostname, for example `https://your-gallery-app.azurewebsites.net/`.
- Confirm `routes[0].pattern` is the intended `photos.picme.am` subdomain and `custom_domain` is `true`.
- Keep the Worker name appropriate for this account and retain the example's disabled request observability setting unless a reviewed redaction/logging plan is in place.

```powershell
npx wrangler login
npx wrangler deploy --config wrangler.jsonc
Set-Location ..
```

Wrangler deploys the Custom Domain route in the selected Cloudflare account. Resolve any pre-existing DNS record conflict for **that subdomain only**. Do not replace root/www records. Cloudflare handles Custom Domain DNS/certificate provisioning as described in its [Custom Domains guide](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/). Keep the final non-secret origin/route configuration recorded; the [Wrangler reference](https://developers.cloudflare.com/workers/wrangler/configuration/) explains the configuration format.

The Worker forwards the original path/query to the fixed Azure origin, uses manual redirect handling so blob SAS redirects reach the browser, and returns private/no-store responses. No Azure storage key belongs in the Worker. Check for zone-wide cache rules that could override the intended behavior; do not cache guest pages/APIs or authorization redirects at a shared edge.

## 7. Point PicmeBooth at the verified address

In PicmeBooth Uploads settings, configure the same Azure account/container, a valid Event ID or the booth's date fallback, and:

```text
https://photos.picme.am/p/{accessToken}
```

Upload a retained test session manually, or enable automatic uploads for a future test capture. The booth's current workflow retains sessions/QRs independently of auto-upload, so seeing a printed QR does not prove the session has reached Azure.

Scan the actual printed QR through the public hostname. Check final-first progress, all selected originals, downloads, and JPEG/GIF behavior for the deployed revisions. Confirm that a second guest token cannot access the first guest's files. Complete the [rollout checklist](operations.md#rollout-checklist) before issuing production prints.

## Cost and availability expectations

F1's free hosting allowance does not make storage, Cold retrieval/operations, or all Cloudflare usage free. Cold is online storage with a 90-day minimum billing period; early deletion/tier changes can incur charges. Archive requires rehydration and is unsuitable for immediate scans. These distinctions are documented in the [Azure access-tier guide](https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview).

Use current region/redundancy/traffic inputs in the [Azure Blob pricing page](https://azure.microsoft.com/en-us/pricing/details/storage/blobs/), [App Service pricing page](https://azure.microsoft.com/en-us/pricing/details/app-service/), and [Cloudflare Workers limits](https://developers.cloudflare.com/workers/platform/limits/). No fixed monthly cost or production availability is promised by this setup.
