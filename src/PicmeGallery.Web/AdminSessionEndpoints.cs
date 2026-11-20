using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace PicmeGallery.Web;

public static class AdminSessionEndpoints
{
    // The group supplies authentication on each route and CSRF validation for mutations.
    public static void MapSessionManagement(this RouteGroupBuilder api)
    {
        api.MapGet("/events/{eventId}/sessions", async (string eventId, string? cursor, string? filter,
            IEventStore events, IEventPhotoStore photos, IDataProtectionProvider protection, IOptions<StorageSafetyOptions> safety, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId)) return Results.NotFound();
            string mode = filter ?? "all";
            if (mode is not ("all" or "visible" or "hidden" or "deleted")) return Results.BadRequest(new { message = "Choose All, Visible, Hidden, or Deleted sessions." });
            var stored = await events.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            string scope = "admin-sessions:" + eventId + ":" + mode;
            if (!Cursors.TryRead(protection, scope, cursor, out string? continuation)) return Results.BadRequest(new { message = "Invalid page. Refresh the sessions." });
            var record = stored.Record;
            var page = await SessionPages.ReadAsync(photos, eventId, continuation,
                session => mode == "deleted"
                    ? record.IsDeleted(session.SessionId) && record.CanPreview(session.SessionId, DateTimeOffset.UtcNow)
                    : !record.IsDeleted(session.SessionId) && (mode == "all" || record.IsHidden(session.SessionId) == (mode == "hidden")), cancellation, safety.Value.MaxPagesPerRequest);
            return Results.Ok(new
            {
                eventId, record.Name, stored.Version,
                hiddenCount = record.HiddenSessionIds?.Count(id => !record.IsDeleted(id)) ?? 0,
                sessions = page.Sessions.Select(session => new
                {
                    sessionId = session.SessionId, hidden = record.IsHidden(session.SessionId),
                    deleteAfterUtc = record.DeletedSessions?.GetValueOrDefault(session.SessionId)?.DeleteAfterUtc,
                    capturedUtc = session.Final.CapturedUtc, contentType = session.Final.ContentType,
                    thumbnailUrl = $"/api/admin/events/{eventId}/sessions/{session.SessionId}/final?view=thumbnail",
                    previewUrl = $"/api/admin/events/{eventId}/sessions/{session.SessionId}/final?view=preview",
                    originalUrl = $"/api/admin/events/{eventId}/sessions/{session.SessionId}/final"
                }),
                nextCursor = Cursors.Write(protection, scope, page.Continuation)
            });
        }).RequireAuthorization();
        api.MapGet("/events/{eventId}/sessions/{sessionId}/final", async (string eventId, string sessionId, string? view,
            IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId) || await events.GetAsync(eventId, cancellation) is not { } stored || !stored.Record.CanPreview(sessionId, DateTimeOffset.UtcNow))
                return Results.NotFound();
            var final = await photos.GetAsync(sessionId, "final", cancellation);
            return final?.EventId == eventId ? Results.Redirect(photos.DownloadUrl(final.Browse(view), false).AbsoluteUri) : Results.NotFound();
        }).RequireAuthorization();
        api.MapPost("/events/{eventId}/sessions/{sessionId}/visibility", async (string eventId, string sessionId,
            VisibilityInput input, IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId)) return Results.NotFound();
            if (input.Hidden is null) return Results.BadRequest(new { message = "Specify whether the session should be hidden." });
            var stored = await events.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            if (stored.Version != input.Version) throw new EventConflictException();
            if (stored.Record.IsDeleted(sessionId)) return Results.NotFound();
            var final = await photos.GetAsync(sessionId, "final", cancellation);
            if (final?.EventId != eventId) return Results.NotFound();
            var hidden = new HashSet<string>(stored.Record.HiddenSessionIds ?? [], StringComparer.Ordinal);
            bool changed = input.Hidden.Value ? hidden.Add(sessionId) : hidden.Remove(sessionId);
            if (changed)
                await events.SaveAsync(stored.Record with { HiddenSessionIds = hidden.Order(StringComparer.Ordinal).ToArray() }, stored.Version, writeAccessPointer: false, cancellation);
            return Results.Ok(new { sessionId, hidden = input.Hidden.Value });
        }).RequireAuthorization();
        api.MapGet("/events/{eventId}/sessions/{sessionId}/originals", async (string eventId, string sessionId,
            IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId) ||
                await events.GetAsync(eventId, cancellation) is not { } stored || !stored.Record.CanPreview(sessionId, DateTimeOffset.UtcNow))
                return Results.NotFound();
            var final = await photos.GetAsync(sessionId, "final", cancellation);
            if (final?.EventId != eventId) return Results.NotFound();
            var gallery = await photos.FindAsync(sessionId, cancellation);
            return Results.Ok(new
            {
                expectedOriginals = final.ExpectedOriginals,
                originals = gallery.Originals.Where(photo => photo.EventId == eventId).Select(photo => new
                {
                    id = photo.Id, previewUrl = $"/api/admin/events/{eventId}/sessions/{sessionId}/originals/{photo.Id}?view=preview",
                    originalUrl = $"/api/admin/events/{eventId}/sessions/{sessionId}/originals/{photo.Id}"
                })
            });
        }).RequireAuthorization();
        api.MapGet("/events/{eventId}/sessions/{sessionId}/originals/{assetId}", async (string eventId, string sessionId, string assetId, string? view,
            IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId) || assetId == "final" || !GalleryContract.ValidAssetId(assetId) ||
                await events.GetAsync(eventId, cancellation) is not { } stored || !stored.Record.CanPreview(sessionId, DateTimeOffset.UtcNow))
                return Results.NotFound();
            var photo = await photos.GetAsync(sessionId, assetId, cancellation);
            return photo?.EventId == eventId ? Results.Redirect(photos.DownloadUrl(photo.Browse(view), false).AbsoluteUri) : Results.NotFound();
        }).RequireAuthorization();
        api.MapPost("/events/{eventId}/sessions/{sessionId}/deletion", async (string eventId, string sessionId,
            DeletionInput input, IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId)) return Results.NotFound();
            if (input.Deleted is null) return Results.BadRequest(new { message = "Specify whether to delete or restore the session." });
            var stored = await events.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            if (stored.Version != input.Version) throw new EventConflictException();
            var deleted = new Dictionary<string, DeletedSession>(stored.Record.DeletedSessions ?? new Dictionary<string, DeletedSession>());
            if (input.Deleted.Value)
            {
                var final = await photos.GetAsync(sessionId, "final", cancellation);
                if (final?.EventId != eventId) return Results.NotFound();
                // Repeated Delete requests do not extend the recovery period.
                deleted.TryAdd(sessionId, new(DateTimeOffset.UtcNow));
            }
            else
            {
                if (!deleted.TryGetValue(sessionId, out var state) || !state.CanRestore(DateTimeOffset.UtcNow))
                    return Results.BadRequest(new { message = "The recovery period has ended or the session is no longer deleted." });
                deleted.Remove(sessionId);
            }
            await events.SaveAsync(stored.Record with { DeletedSessions = deleted }, stored.Version, false, cancellation);
            return Results.Ok(new { sessionId, deleted = input.Deleted.Value });
        }).RequireAuthorization();
    }
    public sealed record DeletionInput(bool? Deleted, string? Version);
    public sealed record VisibilityInput(bool? Hidden, string? Version);
}
