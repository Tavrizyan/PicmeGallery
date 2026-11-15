# Azure App Service quota investigation

Observed November 15, 2026. Prepared for owner review in Azure portal or a support request; no support request has been submitted.

## Problem

A new Linux Free F1 Web App remains in `QuotaExceeded` before its first successful application deployment. Both its application endpoint and SCM endpoint return HTTP 403. Starting and restarting the existing app did not clear the condition.

## Resources

- Subscription: `fdfffc85-91e9-45e6-8355-2e957bd82812` (`MainSubscription`).
- Resource group: `picme-gallery-rg`.
- App: `picme-gallery`; plan: `picme-gallery-plan`.
- Region: Poland Central; SKU F1 / Free; Linux / .NET 10.
- Origin: `https://picme-gallery.azurewebsites.net/`.

## Evidence

- Subscription Enabled; spending limit Off.
- App: state QuotaExceeded, usageState Exceeded, enabled true, availabilityState Normal.
- Plan: provisioningState Succeeded, status Ready, powerState Running, one site.
- Filesystem usage: 348,160 bytes out of 1,073,741,824 bytes.
- Usage API reports zero current CPU/network/memory usage with limit -1; this does not identify the enforced quota. CPU metric query for the preceding three days returned no positive samples.
- LinuxAppDown diagnostics provided no actionable explanation.
- Start and restart completed successfully, but subsequent app state remained QuotaExceeded and SCM returned 403.
- GitHub Actions [run 43454552251](https://github.com/Tavrizyan/PicmeGallery/actions/runs/43454552251): checks/publish/artifact upload passed; Azure OIDC login passed; OneDeploy failed with `Site Disabled (CODE: 403)` on November 13 at approximately 15:48 UTC.

## Requested investigation

Identify the specific enforced quota, its scope, measured usage, limit and reset time, or confirm whether stale quota enforcement/platform provisioning is preventing this new F1 app from running. Explain the recovery available while retaining the existing Free F1 plan and resource identities. No paid upgrade, resource deletion, or storage migration is authorized by this note.

Reference: [Microsoft App Service 403 troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/error-403-web-app-stopped-message).
