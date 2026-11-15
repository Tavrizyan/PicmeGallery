# PicmeGallery deployment record

Original infrastructure verified on 2026-11-13; replacement hosting, authentication and Actions status verified on 2026-11-15. The gallery and Cloudflare proxy were successfully deployed to replacement West Europe hosting on 2026-11-15. Origin and public liveness passed; live storage/guest acceptance remains pending. There is one intended environment, named `gallery` in GitHub Actions.

| Field | Verified value |
| --- | --- |
| Azure subscription | `MainSubscription` / `fdfffc85-91e9-45e6-8355-2e957bd82812` |
| Azure regions | Replacement hosting: West Europe (`westeurope`); storage and resource group metadata: Germany West Central (`germanywestcentral`) |
| Resource group | `picme-gallery-rg` — created |
| Storage account | `picmegallerystorage` — created; StorageV2, Standard LRS, anonymous blob access disabled, TLS 1.2 minimum |
| Container | `photos` — created with public access off |
| App Service plan | `picme-gallery-plan-weu` — West Europe, verified F1 / Free; obsolete Poland plan deleted with owner approval November 15 |
| Web App | `picme-gallery-weu` — Linux, HTTPS only; initially Running / Normal; obsolete Poland app deleted with owner approval November 15 |
| Runtime / startup | `DOTNETCORE\|10.0` / `dotnet PicmeGallery.Web.dll` |
| Assigned Azure origin | `https://picme-gallery-weu.azurewebsites.net/` |
| Deployment Entra application | `picme-gallery-github`; client ID `6615cd78-70f1-4afd-a3cb-dde020c2c0d7` |
| Deployment tenant | `4d5ca826-4d14-4028-b802-d6660459fb81` |
| Deployment federation | GitHub OIDC; subject `repo:Tavrizyan/PicmeGallery:environment:gallery` |
| Deployment authorization | Website Contributor assigned to the service principal, scoped to current Web App `picme-gallery-weu`; obsolete Poland Web App deleted |
| GitHub environment | `gallery` created; main branch only, no reviewer/wait gate; Azure variables and identifier secrets configured and verified |
| GitHub account | `Tavrizyan` / ADMIN reverified on this laptop November 15 |
| Remaining private configuration | New-app private settings owner-confirmed; public guest API unknown-session storage lookup passed (HTTP 200 / waiting). Real-asset/SAS/download checks pending. Both Cloudflare secret names verified present November 15; values and account/zone permissions not verified |
| Deployed commit / retained package | `82e1f7c373ca99b5c0831aa9e11c4a0dc042ea65`; [run 43454552251, attempt 3](https://github.com/Tavrizyan/PicmeGallery/actions/runs/43454552251/attempts/3) succeeded on November 15; publish artifact retained under workflow’s 30-day retention |
| Cloudflare Worker / domain | `picme-gallery-proxy` / `photos.picme.am`; deployed revision `4abb4cf5-07ee-45a7-b4a3-bf1d8df479d1` |
| Liveness / landing | Azure and public `/health`: PicmeGallery / running; public landing HTTP 200, private/no-store, CF cache status DYNAMIC. Public DNS verified via 1.1.1.1 and 8.8.8.8; local negative DNS cache initially required hostname-preserving resolution override |
| Live storage / guest / printed QR checks | Pending |

## Hosting region selection

The Frankfurt Linux F1 plan creation command returned: current limit **0 F1 VMs**, current usage **0**, required new limit **1**. No hosting plan was created by that attempt.

After checking alternative regions, the owner approved a free F1 hosting attempt in Poland Central. Creation succeeded, and a subsequent plan read verified SKU `F1`, tier `Free`. Storage remains in Frankfurt as approved; this places app-to-storage requests across regions and should be considered when measuring latency and transfer costs.

Quota checks also found zero F1 and total regional limits in UAE North and Qatar Central; North Europe and UK South exposed F1 quota but zero total regional limits. Several other European regions, including Poland Central, exposed total regional quota of 30 without an F1-specific entry. The App Service validation API returned success even for blocked Frankfurt, so it was insufficient to establish actual creation eligibility. See Microsoft's [App Service quota guidance](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/troubleshoot-non-zone-redundant-quota-requests).

## Remaining setup

Owner authorized another region on Free F1. The West Europe replacement plan/app were created and the new app initially reported Running / Normal; SCM returned 401 rather than quota-related 403. GitHub variables now target the new app/origin, and the existing deployment identity has Website Contributor scoped to it. Attempt 3 of the retained-package deployment run succeeded, including Azure deployment, origin liveness, Worker deployment and public liveness. Owner confirmed configuring storage settings in the new app; public unknown-session storage lookup returned HTTP 200 / waiting. See the [current resume checkpoint](work-state.md#current-resume-checkpoint--2026-11-15).

1. Configure storage credentials privately in Azure and PicmeBooth; do not put their values in this record.
2. Retain the successful deployment run/artifact and Worker revision for rollback. Both Cloudflare secrets are configured and Worker/Custom Domain deployment passed.
3. Complete the [rollout checklist](operations.md#rollout-checklist) after successful origin/public liveness checks.

Obsolete Poland app `picme-gallery` and plan `picme-gallery-plan` were deleted with explicit owner approval on November 15. Resource group listing now contains only Frankfurt storage and the West Europe app/plan; public health returned running after cleanup.

Resources created earlier in a different subscription remain outside this environment. Cleanup has not been authorized or performed.
