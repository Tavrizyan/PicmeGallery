# PicmeGallery deployment record

Verified on 2026-11-13. Azure hosting and storage resources exist; no gallery application package or public proxy is deployed by this rollout yet. There is one intended environment, named `gallery` in GitHub Actions.

| Field | Verified value |
| --- | --- |
| Azure subscription | `MainSubscription` / `fdfffc85-91e9-45e6-8355-2e957bd82812` |
| Azure regions | Hosting: Poland Central (`polandcentral`); storage and resource group metadata: Germany West Central (`germanywestcentral`) |
| Resource group | `picme-gallery-rg` — created |
| Storage account | `picmegallerystorage` — created; StorageV2, Standard LRS, anonymous blob access disabled, TLS 1.2 minimum |
| Container | `photos` — created with public access off |
| App Service plan | `picme-gallery-plan` — created in Poland Central; verified F1 / Free |
| Web App | `picme-gallery` — created; Linux, HTTPS only |
| Runtime / startup | `DOTNETCORE\|10.0` / `dotnet PicmeGallery.Web.dll` |
| Assigned Azure origin | `https://picme-gallery.azurewebsites.net/` |
| Deployment Entra application | `picme-gallery-github`; client ID `6615cd78-70f1-4afd-a3cb-dde020c2c0d7` |
| Deployment tenant | `4d5ca826-4d14-4028-b802-d6660459fb81` |
| Deployment federation | GitHub OIDC; subject `repo:Tavrizyan/PicmeGallery:environment:gallery` |
| Deployment authorization | Website Contributor assigned to the service principal, scoped only to Web App `picme-gallery` |
| GitHub environment | `gallery` created; main branch only, no reviewer/wait gate; Azure variables and identifier secrets configured and verified |
| GitHub account | `Tavrizyan`, verified ADMIN permission |
| Remaining private configuration | Owner reports Azure storage app settings saved; live storage test pending. GitHub Cloudflare account ID/API-token secrets await owner entry |
| Deployed commit / retained rollback package | None from this rollout |
| Cloudflare Worker / domain | Intended: `picme-gallery-proxy` / `photos.picme.am`; not deployed by this rollout |
| Live storage / guest / printed QR checks | Pending |

## Hosting region selection

The Frankfurt Linux F1 plan creation command returned: current limit **0 F1 VMs**, current usage **0**, required new limit **1**. No hosting plan was created by that attempt.

After checking alternative regions, the owner approved a free F1 hosting attempt in Poland Central. Creation succeeded, and a subsequent plan read verified SKU `F1`, tier `Free`. Storage remains in Frankfurt as approved; this places app-to-storage requests across regions and should be considered when measuring latency and transfer costs.

Quota checks also found zero F1 and total regional limits in UAE North and Qatar Central; North Europe and UK South exposed F1 quota but zero total regional limits. Several other European regions, including Poland Central, exposed total regional quota of 30 without an F1-specific entry. The App Service validation API returned success even for blocked Frankfurt, so it was insufficient to establish actual creation eligibility. See Microsoft's [App Service quota guidance](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/troubleshoot-non-zone-redundant-quota-requests).

## Remaining setup

1. Configure storage credentials privately in Azure and PicmeBooth; do not put their values in this record.
2. Have the owner privately add Cloudflare deployment credentials to the configured GitHub `gallery` environment, following the [deployment guide](deployment.md#github-actions-deployment). Azure/GitHub authentication setup is complete; GitHub account `Tavrizyan` has verified ADMIN permission.
3. Review and authorize the first deployment, then complete the [rollout checklist](operations.md#rollout-checklist).

Resources created earlier in a different subscription remain outside this environment. Cleanup has not been authorized or performed.
