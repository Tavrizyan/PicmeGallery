using Azure;
using Azure.Storage.Blobs.Models;

namespace PicmeGallery.Web;

public sealed class AzureEventStore(IConfiguration configuration) : IEventStore
{
    private const string Prefix = "gallery/events/";
    public async Task<EventListPage> ListAsync(string? continuation, CancellationToken cancellation)
    {
        var container = BlobStorage.Container(configuration);
        await foreach (var page in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, Prefix, cancellation).AsPages(continuation, 30))
        {
            var records = new List<StoredEvent>();
            foreach (var blob in page.Values)
            {
                string eventId = Path.GetFileNameWithoutExtension(blob.Name);
                if (GalleryContract.ValidEventId(eventId) && await GetAsync(eventId, cancellation) is { } record) records.Add(record);
            }
            return new(records, page.ContinuationToken);
        }
        return new([], null);
    }
    public async Task<StoredEvent?> GetAsync(string eventId, CancellationToken cancellation)
    {
        if (!GalleryContract.ValidEventId(eventId)) return null;
        try
        {
            var result = (await BlobStorage.Container(configuration).GetBlobClient(Prefix + eventId + ".json").DownloadContentAsync(cancellation)).Value;
            var record = result.Content.ToObjectFromJson<EventRecord>();
            return record?.EventId == eventId ? new(record, result.Details.ETag.ToString()) : null;
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }
    public async Task<StoredEvent?> ResolveAsync(string tokenHash, CancellationToken cancellation)
    {
        if (!GalleryContract.ValidHash(tokenHash)) return null;
        try
        {
            var result = await BlobStorage.Container(configuration).GetBlobClient("gallery/access/" + tokenHash + ".json").DownloadContentAsync(cancellation);
            string eventId = result.Value.Content.ToObjectFromJson<string>() ?? "";
            var stored = await GetAsync(eventId, cancellation);
            // The event record is authoritative, so old lookup blobs never authorize replaced/revoked links.
            return stored?.Record.TokenHash == tokenHash ? stored : null;
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }
    public async Task SaveAsync(EventRecord record, string? expectedVersion, CancellationToken cancellation)
    {
        if (!GalleryContract.ValidEventId(record.EventId)) throw new ArgumentException("Invalid event ID.");
        var container = BlobStorage.Container(configuration);
        var headers = new BlobHttpHeaders { ContentType = "application/json", CacheControl = "private, no-store" };
        // Write the lookup before activating the record; an interrupted or conflicting write cannot grant access.
        if (record.TokenHash is not null)
            await container.GetBlobClient("gallery/access/" + record.TokenHash + ".json").UploadAsync(BinaryData.FromObjectAsJson(record.EventId),
                new BlobUploadOptions { HttpHeaders = headers, AccessTier = AccessTier.Hot }, cancellation);
        try
        {
            await container.GetBlobClient(Prefix + record.EventId + ".json").UploadAsync(BinaryData.FromObjectAsJson(record),
                new BlobUploadOptions
                {
                    HttpHeaders = headers, AccessTier = AccessTier.Hot,
                    Conditions = expectedVersion is null ? new BlobRequestConditions { IfNoneMatch = ETag.All } : new BlobRequestConditions { IfMatch = new ETag(expectedVersion) }
                }, cancellation);
        }
        catch (RequestFailedException error) when (error.Status is 409 or 412) { throw new EventConflictException(); }
    }
}
