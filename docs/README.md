# PicmeGallery documentation

Maintainer reference, updated 2026-11-12. These documents preserve the requirements and decisions behind the application so its operation does not depend on the original chat.

| Document | Use it for |
| --- | --- |
| [Architecture](architecture.md) | Why this application exists; component responsibilities; permanent QR links; Azure paths, tags, metadata, and API behavior; deferred work. |
| [Deployment](deployment.md) | Configure real Azure storage, run locally, publish to Azure App Service F1, and serve the public hostname through Cloudflare. |
| [Operations](operations.md) | Validate a rollout, diagnose failures, retain printed links, rotate credentials, and maintain the application. |
| [Deployment record template](deployment-record.example.md) | Record the verified environment, public/origin addresses, deployed revisions, and rollout checks without storing secrets. |
| [Current deployment record](deployment-record.md) | Verified hosting/storage resources, region selection, and remaining setup. |
| [Work state / laptop handoff](work-state.md) | Resume the unfinished deployment with verified identities, approvals, local changes, checks, and next steps. |
| [Deployment documentation audit](deployment-audit.md) | Verified requirements, remaining configuration gaps, and rollout checks that have not passed yet. |

## Project identity

- Repository: [Tavrizyan/PicmeGallery](https://github.com/Tavrizyan/PicmeGallery).
- Working branch: `main`. Continue changes on `main`; create another branch only if the owner explicitly requests one.
- Companion desktop repository: [Tavrizyan/PicmeBooth](https://github.com/Tavrizyan/PicmeBooth).
- Proposed permanent public address: `https://photos.picme.am/p/{accessToken}`.
- Initial hosting decision: a separate Azure App Service Web App on Free F1, with a Cloudflare Worker for the public subdomain.
- Runtime: ASP.NET Core on .NET 10; the same process serves the guest UI and API.
- Persistent photo storage: private Azure Blob Storage, with explicit Cold-tier uploads by PicmeBooth.
- Application database: none. The gallery does not store an album registry or a token-to-session mapping.

The public hostname is a proposed/configured address, not evidence that a deployment is live. The [current deployment record](deployment-record.md) documents the verified subscription, hosting, and storage resources; application deployment and live rollout checks remain pending.

## Scope and implementation status

The repository contains the guest gallery, Azure read/signing logic, a Cloudflare proxy, automated checks, and a deployment workflow. After the one-time resource/authentication setup in the [deployment guide](deployment.md#github-actions-deployment), successful pushes to `main` deploy the checked application to Azure and publish the Worker. The workflow does not provision Azure resources.

The current local integration work also supports animated final GIFs. Deploy the matching booth/gallery revisions together and verify the published revision before relying on GIF sessions. The [storage contract](architecture.md#storage-contract-version-1) describes both JPEG and GIF finals.

The existing `Picme.am` application is a separate project. Its code, database, root/www domain routing, and deployment are outside this work. Owner/admin event galleries, resizing, cloud deletion, and guest accounts remain future work. Local emulator support was considered and reverted; use real Azure cloud storage for integration testing.
