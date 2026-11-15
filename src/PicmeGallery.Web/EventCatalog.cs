using Microsoft.Extensions.Options;

namespace PicmeGallery.Web;

public sealed record AdminEventCursor(DateTimeOffset CreatedUtc, string EventId);

// This catalog reads only small, explicitly created event-link records. It never enumerates photos.
public sealed class EventCatalog(IEventStore events, IOptions<StorageSafetyOptions> safety)
{
    public async Task<IReadOnlyList<StoredEvent>> ListAsync(CancellationToken cancellation)
    {
        var catalog = new Dictionary<string, StoredEvent>(StringComparer.Ordinal);
        string? continuation = null;
        var guard = new PaginationGuard(safety.Value.MaxPagesPerRequest);
        do
        {
            guard.BeforeRead(continuation, cancellation);
            var page = await events.ListAsync(continuation, cancellation);
            foreach (var stored in page.Events) catalog[stored.Record.EventId] = stored;
            continuation = page.Continuation;
        } while (!string.IsNullOrEmpty(continuation));
        return catalog.Values.OrderByDescending(item => item.Record.CreatedUtc)
            .ThenBy(item => item.Record.EventId, StringComparer.Ordinal).ToArray();
    }
}
