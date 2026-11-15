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
            if (mode is not ("all" or "visible" or "hidden")) return Results.BadRequest(new { message = "Choose All, Visible, or Hidden sessions." });
            var stored = await events.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            string scope = "admin-sessions:" + eventId + ":" + mode;
            if (!Cursors.TryRead(protection, scope, cursor, out string? continuation)) return Results.BadRequest(new { message = "Invalid page. Refresh the sessions." });
            var record = stored.Record;
            var page = await SessionPages.ReadAsync(photos, eventId, continuation,
                session => mode == "all" || record.IsHidden(session.SessionId) == (mode == "hidden"), cancellation, safety.Value.MaxPagesPerRequest);
            return Results.Ok(new
            {
                eventId, record.Name, stored.Version,
                hiddenCount = record.HiddenSessionIds?.Count ?? 0,
                sessions = page.Sessions.Select(session => new
                {
                    sessionId = session.SessionId, hidden = record.IsHidden(session.SessionId),
                    capturedUtc = session.Final.CapturedUtc, contentType = session.Final.ContentType,
                    previewUrl = $"/api/admin/events/{eventId}/sessions/{session.SessionId}/final"
                }),
                nextCursor = Cursors.Write(protection, scope, page.Continuation)
            });
        }).RequireAuthorization();
        api.MapGet("/events/{eventId}/sessions/{sessionId}/final", async (string eventId, string sessionId,
            IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId) || await events.GetAsync(eventId, cancellation) is null)
                return Results.NotFound();
            var final = await photos.GetAsync(sessionId, "final", cancellation);
            return final?.EventId == eventId ? Results.Redirect(photos.DownloadUrl(final, false).AbsoluteUri) : Results.NotFound();
        }).RequireAuthorization();
        api.MapPost("/events/{eventId}/sessions/{sessionId}/visibility", async (string eventId, string sessionId,
            VisibilityInput input, IEventStore events, IPhotoStore photos, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || !GalleryContract.ValidHash(sessionId)) return Results.NotFound();
            if (input.Hidden is null) return Results.BadRequest(new { message = "Specify whether the session should be hidden." });
            var stored = await events.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            if (stored.Version != input.Version) throw new EventConflictException();
            var final = await photos.GetAsync(sessionId, "final", cancellation);
            if (final?.EventId != eventId) return Results.NotFound();
            var hidden = new HashSet<string>(stored.Record.HiddenSessionIds ?? [], StringComparer.Ordinal);
            bool changed = input.Hidden.Value ? hidden.Add(sessionId) : hidden.Remove(sessionId);
            if (changed)
                await events.SaveAsync(stored.Record with { HiddenSessionIds = hidden.Order(StringComparer.Ordinal).ToArray() }, stored.Version, cancellation);
            return Results.Ok(new { sessionId, hidden = input.Hidden.Value });
        }).RequireAuthorization();
    }
    public sealed record VisibilityInput(bool? Hidden, string? Version);
}
