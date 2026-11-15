using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;

namespace PicmeGallery.Web;

public static class OwnerEndpoints
{
    public static void MapOwnerGallery(this WebApplication app)
    {
        app.MapGet("/e/{token}", (string token, IWebHostEnvironment env) => GalleryContract.TokenHash(token) is null ? Results.NotFound()
            : Results.File(Path.Combine(env.WebRootPath, "event.html"), "text/html"));
        app.MapGet("/e/{token}/s/{sessionId}", (string token, string sessionId, IWebHostEnvironment env) => GalleryContract.TokenHash(token) is null || !GalleryContract.ValidHash(sessionId)
            ? Results.NotFound() : Results.File(Path.Combine(env.WebRootPath, "index.html"), "text/html"));
        var api = app.MapGroup("/api/event/{token}");
        api.AddEndpointFilter(async (context, next) =>
        {
            string? hash = GalleryContract.TokenHash(context.HttpContext.Request.RouteValues["token"]?.ToString() ?? "");
            var store = context.HttpContext.RequestServices.GetRequiredService<IEventStore>();
            var stored = hash is null ? null : await store.ResolveAsync(hash, context.HttpContext.RequestAborted);
            if (stored is null) return Results.NotFound(new { message = "This event link is unavailable. Please ask the organizer for a current link." });
            context.HttpContext.Items["OwnerEvent"] = stored.Record;
            return await next(context);
        });
        api.MapGet("", async (string token, string? cursor, HttpContext context, IEventPhotoStore photos, IDataProtectionProvider protection, CancellationToken cancellation) =>
        {
            var record = Event(context);
            if (!Cursors.TryRead(protection, "event:" + record.EventId, cursor, out string? continuation)) return Results.BadRequest(new { message = "Invalid page. Refresh the gallery." });
            var page = await photos.ListFinalsAsync(record.EventId, continuation, cancellation);
            return Results.Ok(new
            {
                name = record.Name,
                sessions = page.Sessions.Select(session => new
                {
                    sessionId = session.SessionId, final = Describe(token, session.SessionId, session.Final),
                    sessionPath = $"/e/{token}/s/{session.SessionId}"
                }),
                nextCursor = Cursors.Write(protection, "event:" + record.EventId, page.Continuation),
                downloadAllUrl = $"/api/event/{token}/download"
            });
        });
        api.MapGet("/sessions/{sessionId}", async (string token, string sessionId, HttpContext context, IPhotoStore store, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidHash(sessionId)) return Results.NotFound();
            var photos = await store.FindAsync(sessionId, cancellation);
            if (photos.Final?.EventId != Event(context).EventId) return Results.NotFound();
            return Results.Ok(new
            {
                state = photos.Originals.Count >= photos.Final.ExpectedOriginals ? "ready" : "uploading",
                eventId = photos.Final.EventId,
                expectedOriginals = photos.Final.ExpectedOriginals, uploadedOriginals = photos.Originals.Count,
                final = Describe(token, sessionId, photos.Final), originals = photos.Originals.Select(photo => Describe(token, sessionId, photo))
            });
        });
        api.MapGet("/media/{sessionId}/{id}", async (string sessionId, string id, bool? download, bool? file, HttpContext context, IPhotoStore store, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidHash(sessionId) || !GalleryContract.ValidAssetId(id)) return Results.NotFound();
            var final = await store.GetAsync(sessionId, "final", cancellation);
            if (final?.EventId != Event(context).EventId) return Results.NotFound();
            var photo = id == "final" ? final : await store.GetAsync(sessionId, id, cancellation);
            if (photo is null || photo.EventId != final.EventId || photo.ExpectedOriginals != final.ExpectedOriginals) return Results.NotFound();
            if (file == true) return Results.Stream(await store.OpenReadAsync(photo, cancellation), photo.ContentType,
                fileDownloadName: download == true ? photo.FileName : null);
            return Results.Redirect(store.DownloadUrl(photo, download == true).AbsoluteUri);
        });
        api.MapGet("/download", (HttpContext context, IEventPhotoStore events, IPhotoStore photos, ILoggerFactory logging) =>
        {
            var record = Event(context);
            // .NET 10 entry streams can synchronously write their small closing data descriptor,
            // even when opened/disposed asynchronously. Photo bytes and archive finalization use async IO.
            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>() is { } bodyControl)
                bodyControl.AllowSynchronousIO = true;
            var cancellation = context.RequestAborted;
            return Results.Stream(async output =>
            {
                try
                {
                    await using var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellation);
                    string? cursor = null;
                    var seen = new HashSet<string>();
                    do
                    {
                        var page = await events.ListFinalsAsync(record.EventId, cursor, cancellation);
                        foreach (var session in page.Sessions)
                        {
                            if (!seen.Add(session.SessionId)) continue;
                            var gallery = await photos.FindAsync(session.SessionId, cancellation);
                            if (gallery.Final?.EventId != record.EventId) continue;
                            foreach (var photo in new[] { gallery.Final }.Concat(gallery.Originals))
                            {
                                cancellation.ThrowIfCancellationRequested();
                                var entry = archive.CreateEntry(session.SessionId + "/" + photo.FileName, CompressionLevel.NoCompression);
                                await using var source = await photos.OpenReadAsync(photo, cancellation);
                                await using var destination = await entry.OpenAsync(cancellation);
                                await source.CopyToAsync(destination, cancellation);
                            }
                        }
                        cursor = page.Continuation;
                    } while (cursor is not null);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { context.Abort(); }
                catch (Exception error)
                {
                    // A partial archive must not look like a successful download. Never log token-bearing URLs.
                    logging.CreateLogger("EventDownload").LogWarning("Event archive interrupted ({FailureType}).", error.GetType().Name);
                    context.Abort();
                }
            }, "application/zip", record.EventId + "-all-photos.zip");
        }).RequireRateLimiting("event-zip");
    }
    private static EventRecord Event(HttpContext context) => (EventRecord)context.Items["OwnerEvent"]!;
    private static object Describe(string token, string sessionId, PhotoAsset photo) => new
    {
        id = photo.Id, bytes = photo.Bytes, capturedUtc = photo.CapturedUtc, fileName = photo.FileName, contentType = photo.ContentType,
        previewUrl = $"/api/event/{token}/media/{sessionId}/{photo.Id}",
        downloadUrl = $"/api/event/{token}/media/{sessionId}/{photo.Id}?download=true",
        fileUrl = $"/api/event/{token}/media/{sessionId}/{photo.Id}?file=true"
    };
}
