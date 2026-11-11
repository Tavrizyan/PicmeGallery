using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PicmeGallery.Web;

public sealed class AzurePhotoStore(IConfiguration configuration) : IPhotoStore
{
    private BlobContainerClient Container()
    {
        string? connection = configuration["Azure:ConnectionString"], name = configuration["Azure:Container"];
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Azure storage has not been configured.");
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 1; options.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);
        var container = new BlobServiceClient(connection, options).GetBlobContainerClient(name);
        if (container.Uri.Scheme != "https") throw new InvalidOperationException("Azure requires HTTPS.");
        return container;
    }
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
            var photo = await GetAsync(hash, id, token);
            if (photo is not null && photo.EventId == final.EventId && photo.ExpectedOriginals == final.ExpectedOriginals) originals[photo.Index] = photo;
            if (originals.Count >= final.ExpectedOriginals) break;
        }
        return new GuestPhotos(final, originals.Values.OrderBy(p => p.Index).ToArray());
    }
    public async Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken token)
    {
        if (!GalleryContract.ValidAssetId(id)) return null;
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
                return asset?.BlobName == name ? asset : null;
            }
            catch (RequestFailedException error) when (error.Status == 404) { }
        }
        return null;
    }
    public Uri DownloadUrl(PhotoAsset asset, bool attachment)
    {
        var blob = Container().GetBlobClient(asset.BlobName);
        if (!blob.CanGenerateSasUri) throw new InvalidOperationException("Configure server-side credentials capable of signing individual image reads.");
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5))
        {
            Protocol = SasProtocol.Https, CacheControl = "private, no-store",
            ContentDisposition = (attachment ? "attachment" : "inline") + "; filename=\"picme-" +
                (asset.Id == "final" ? Path.GetFileName(asset.BlobName) : "original-" + asset.Id) + "\""
        };
        return blob.GenerateSasUri(sas);
    }
}
