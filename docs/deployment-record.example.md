# PicmeGallery deployment record template

Template only: no live environment is asserted by these example fields. Copy it for the verified environment, replacing `Not recorded` values. Do not add connection strings, account keys, tokens, SAS URLs, or real guest photos.

| Field | Verified value |
| --- | --- |
| Environment / purpose | Not recorded |
| Verified on / responsible maintainer | Not recorded |
| Repository branch | `main` |
| Deployed gallery commit | Not recorded |
| Compatible PicmeBooth commit | Not recorded |
| Supported final media | Verify JPEG; verify GIF if enabled in the deployed revisions |
| Azure subscription name/reference | Not recorded |
| Azure resource group | Not recorded |
| Azure Web App name | Not recorded |
| App Service plan / OS / region | Selected starting plan: F1; actual values not recorded |
| .NET runtime | Required: .NET 10; actual setting not recorded |
| Assigned Azure HTTPS origin | Not recorded |
| Azure storage account / region / redundancy | Not recorded |
| Private container | Example: `photos`; actual value not recorded |
| Asset access tier / schema | Required: Cold / version 1 |
| Storage secret location | Web App app setting and booth protected settings; never record its value |
| Cloudflare account / zone reference | Not recorded |
| Worker name / deployed revision | Not recorded |
| `AZURE_ORIGIN` variable | Not recorded; must match the assigned HTTPS origin |
| Public gallery hostname | Proposed: `photos.picme.am`; verified value not recorded |
| Printed route / booth guest URL pattern | `https://<verified-host>/p/{accessToken}` |
| Last full rollout checklist result | Not recorded |
| Last old-QR / expired-SAS recovery check | Not recorded |
| Backup / recovery owner and reference | Not recorded |
| Previous package/configuration for rollback | Not recorded |

## Rollout evidence

Record dates, revision references, and pass/fail results for the [operations checklist](operations.md#rollout-checklist). Keep test tokens in a private test record and refer to that record without copying its secrets here.

## Changes since the previous deployment

Describe hostname/origin changes, credential-rotation completion, contract compatibility, and unresolved rollout issues. Include the next action and owner for any outstanding check.
