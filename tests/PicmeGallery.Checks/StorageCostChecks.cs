using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PicmeGallery.Web;

namespace PicmeGallery.Checks;

internal static class StorageCostChecks
{
    public static async Task RunAsync()
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        string hash = new('a', 64);
        var container = new Photos(hash);
        using var store = new AzurePhotoStore(container);
        var final = await store.GetAsync(hash, "final", CancellationToken.None);
        await store.GetAsync(hash, "final", CancellationToken.None);
        Check(final is not null && container.Properties == 1 && container.Tags == 1, "Validated metadata is reused without more Azure properties/tag reads");
        Check(final!.Browse("thumbnail").BlobName == final.BlobName && final.Browse("preview").BlobName == final.BlobName,
            "Old booth uploads without variant metadata use the original without derivative probes");
        var photo = final with { HasThumbnail = true, HasPreview = true };
        Check(photo.Browse("thumbnail").BlobName == $"sessions/{hash}/thumbnails/final.jpg" &&
            photo.Browse("preview").BlobName == $"sessions/{hash}/previews/final.jpg", "Photo variants use deterministic session-scoped paths");
        var gif = photo with { BlobName = $"sessions/{hash}/final.gif", MediaType = "image/gif" };
        Check(gif.Browse("thumbnail").ContentType == "image/jpeg" && gif.Browse("preview").ContentType == "image/gif" &&
            gif.Browse("preview").BlobName.EndsWith("/previews/final.gif"), "GIF grid is still while Preview remains animated");
        var png = photo with { Id = "002.png", BlobName = $"sessions/{hash}/originals/002.png", MediaType = "image/png" };
        Check(png.Browse("preview").BlobName.EndsWith("/previews/002.jpg") && png.ContentType == "image/png", "Reduced JPEG preview preserves original PNG download descriptor");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        bool canceled = false;
        try { await store.GetAsync(hash, "final", cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "Cached reads still respect cancellation");
        var partial = await store.FindAsync(hash, CancellationToken.None);
        Check(partial.Originals.Count == 1 && container.Properties == 2 && container.Tags == 2 && container.PrefixLists == 1,
            "Incomplete upload fallback does not revalidate already found originals");
        Check(await store.GetAsync(hash, "002.png", CancellationToken.None) is null, "Pending originals are not exposed");
        container.SecondAvailable = true;
        var uploaded = await store.GetAsync(hash, "002.png", CancellationToken.None);
        Check(uploaded is not null && uploaded.HasThumbnail && uploaded.HasPreview, "Missing originals are not negatively cached and newly advertised variants are recognized");
        Console.WriteLine($"PASS: {count} storage cost/variant/cache checks.");
    }

    private sealed class Photos(string hash) : BlobContainerClient
    {
        public int Properties, Tags, PrefixLists;
        public bool SecondAvailable;
        public override BlobClient GetBlobClient(string name) => new Photo(this, name, hash);
        public override AsyncPageable<TaggedBlobItem> FindBlobsByTagsAsync(string filter, CancellationToken cancellationToken = default) =>
            AsyncPageable<TaggedBlobItem>.FromPages([Page<TaggedBlobItem>.FromValues(
                [BlobsModelFactory.TaggedBlobItem("photos", $"sessions/{hash}/originals/001.jpg", new Dictionary<string, string>())], null, new Reply())]);
        public override AsyncPageable<BlobItem> GetBlobsAsync(BlobTraits traits = BlobTraits.None, BlobStates states = BlobStates.None,
            string? prefix = null, CancellationToken cancellationToken = default)
        {
            PrefixLists++;
            return AsyncPageable<BlobItem>.FromPages([Page<BlobItem>.FromValues(
                [BlobsModelFactory.BlobItem(name: $"sessions/{hash}/originals/001.jpg")], null, new Reply())]);
        }
    }
    private sealed class Photo(Photos owner, string name, string hash) : BlobClient
    {
        public override Task<Response<BlobProperties>> GetPropertiesAsync(BlobRequestConditions? conditions = null, CancellationToken cancellationToken = default)
        {
            owner.Properties++;
            if (name.EndsWith("002.png") && !owner.SecondAvailable) throw new RequestFailedException(404, "Pending upload");
            bool final = name.EndsWith("final.jpg");
            var metadata = new Dictionary<string, string>
            {
                ["schemaversion"] = "1", ["contentsha256"] = new string('b', 64), ["expectedoriginalcount"] = "2",
                ["originalindex"] = final ? "0" : name.EndsWith("001.jpg") ? "1" : "2"
            };
            if (owner.SecondAvailable) { metadata["thumbnailversion"] = "1"; metadata["previewversion"] = "1"; }
            return Task.FromResult(Response.FromValue(BlobsModelFactory.BlobProperties(contentLength: 120,
                contentType: name.EndsWith(".png") ? "image/png" : "image/jpeg", accessTier: "Cold", metadata: metadata), new Reply()));
        }
        public override Task<Response<GetBlobTagResult>> GetTagsAsync(BlobRequestConditions? conditions = null, CancellationToken cancellationToken = default)
        {
            owner.Tags++;
            return Task.FromResult(Response.FromValue(BlobsModelFactory.GetBlobTagResult(new Dictionary<string, string>
            { ["EventId"] = "test-event", ["GuestKeyHash"] = hash, ["Kind"] = name.EndsWith("final.jpg") ? "Final" : "Original" }), new Reply()));
        }
    }
    private sealed class Reply : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }
}
