using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Caching.Memory;

namespace PicmeGallery.Web;

public sealed class AzurePhotoStore : IPhotoStore, IEventPhotoStore, ISessionPhotoDeletion, IDisposable
{
    private readonly Func<BlobContainerClient> container;
    private readonly MemoryCache metadataCache = new(new MemoryCacheOptions { SizeLimit = 4096 });
    public AzurePhotoStore(IConfiguration configuration) => container = () => BlobStorage.Container(configuration);
    public AzurePhotoStore(BlobContainerClient client) => container = () => client;
    private BlobContainerClient Container() => container();
    public void Dispose() => metadataCache.Dispose();
    public async Task<GuestPhotos> FindAsync(string hash, CancellationToken token)
    {
        var container = Container();
        var final = await GetAsync(hash, "final", token);
        if (final is null) return new GuestPhotos(null, []);
        var originals = new Dictionary<int, PhotoAsset>();
        // Hash is server-generated hexadecimal, never a caller-supplied query fragment.
        await foreach (var tagged in container.FindBlobsByTagsAsync($"\"GuestKeyHash\" = '{hash}'", token))
        {
            string prefix = "sessions/" + hash + "/originals/";
            if (!tagged.BlobName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string id = tagged.BlobName[prefix.Length..];
            if (id == "final" || !GalleryContract.ValidAssetId(id)) continue;
            if (originals.Values.Any(photo => photo.Id == id)) continue;
            var photo = await GetAsync(hash, id, token);
            if (photo is not null && photo.EventId == final.EventId && photo.ExpectedOriginals == final.ExpectedOriginals) originals[photo.Index] = photo;
            if (originals.Count >= final.ExpectedOriginals) break;
        }
        // Azure's tag index is asynchronous. Prefix listing keeps committed originals visible while it catches up.
        if (originals.Count < final.ExpectedOriginals)
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, "sessions/" + hash + "/originals/", token))
        {
            string id = blob.Name[("sessions/" + hash + "/originals/").Length..];
            if (id == "final" || !GalleryContract.ValidAssetId(id)) continue;
            if (originals.Values.Any(photo => photo.Id == id)) continue;
            var photo = await GetAsync(hash, id, token);
            if (photo is not null && photo.EventId == final.EventId && photo.ExpectedOriginals == final.ExpectedOriginals) originals[photo.Index] = photo;
            if (originals.Count >= final.ExpectedOriginals) break;
        }
        return new GuestPhotos(final, originals.Values.OrderBy(p => p.Index).ToArray());
    }
    public async Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!GalleryContract.ValidHash(hash) || !GalleryContract.ValidAssetId(id)) return null;
        string key = hash + "/" + id;
        if (metadataCache.TryGetValue(key, out PhotoAsset? cached)) return cached;
        var container = Container();
        string[] names = id == "final" ? [GalleryContract.BlobName(hash, id), "sessions/" + hash + "/final.gif"] : [GalleryContract.BlobName(hash, id)];
        foreach (string name in names)
        {
            var blob = container.GetBlobClient(name);
            try
            {
                var properties = (await blob.GetPropertiesAsync(cancellationToken: token)).Value;
                var tags = (await blob.GetTagsAsync(cancellationToken: token)).Value.Tags;
                var asset = GalleryContract.Parse(hash, id, properties.ContentLength, properties.ContentType,
                    properties.AccessTier?.ToString(), tags, properties.Metadata);
                if (asset?.BlobName != name) return null;
                // Uploads are immutable; cache only successful validation, never access/deletion state or pending assets.
                metadataCache.Set(key, asset, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
                return asset;
            }
            catch (RequestFailedException error) when (error.Status == 404) { }
        }
        return null;
    }
    public async Task<EventPhotoPage> ListFinalsAsync(string eventId, string? continuation, CancellationToken cancellation)
    {
        if (!GalleryContract.ValidEventId(eventId)) throw new ArgumentException("Invalid event ID.");
        var container = Container();
        await foreach (var page in container.FindBlobsByTagsAsync($"\"EventId\" = '{eventId}' AND \"Kind\" = 'Final'", cancellation).AsPages(continuation, 24))
        {
            var sessions = new Dictionary<string, EventFinal>();
            foreach (var candidate in page.Values)
            {
                var parts = candidate.BlobName.Split('/');
                if (parts.Length != 3 || parts[0] != "sessions" || !GalleryContract.ValidHash(parts[1]) || parts[2] is not ("final.jpg" or "final.gif")) continue;
                var photo = await GetAsync(parts[1], "final", cancellation);
                if (photo?.EventId == eventId) sessions[parts[1]] = new(parts[1], photo);
            }
            return new(sessions.Values.ToArray(), string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken);
        }
        return new([], null);
    }
    public async Task DeleteAsync(string sessionId, CancellationToken cancellation)
    {
        if (!GalleryContract.ValidHash(sessionId)) throw new ArgumentException("Invalid session ID.");
        var container = Container();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, "sessions/" + sessionId + "/", cancellation))
        {
            await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellation);
            string id = blob.Name.StartsWith("sessions/" + sessionId + "/originals/", StringComparison.Ordinal) ? Path.GetFileName(blob.Name) : "final";
            metadataCache.Remove(sessionId + "/" + id);
        }
    }
    public async Task<Stream> OpenReadAsync(PhotoAsset asset, CancellationToken token)
    {
        var download = await Container().GetBlobClient(asset.BlobName).DownloadStreamingAsync(cancellationToken: token);
        return download.Value.Content;
    }
    public Uri DownloadUrl(PhotoAsset asset, bool attachment)
    {
        var blob = Container().GetBlobClient(asset.BlobName);
        if (!blob.CanGenerateSasUri) throw new InvalidOperationException("Configure server-side credentials capable of signing individual image reads.");
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5))
        {
            Protocol = SasProtocol.Https, CacheControl = "private, no-store",
            ContentDisposition = (attachment ? "attachment" : "inline") + "; filename=\"" + asset.FileName + "\""
        };
        return blob.GenerateSasUri(sas);
    }
}
