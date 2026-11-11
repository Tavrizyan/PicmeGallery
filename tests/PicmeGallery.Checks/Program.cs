using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using PicmeGallery.Web;

namespace PicmeGallery.Checks;

internal static class Checks
{
    private static int count;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static async Task Main()
    {
        string token = Token(), otherToken = Token();
        string hash = GalleryContract.TokenHash(token)!;
        Check(hash.Length == 64, "Valid booth token resolves to hex hash");
        Check(GalleryContract.TokenHash("42") is null, "Sequential session IDs rejected");
        Check(GalleryContract.TokenHash(new string('B', 43)) is null, "Noncanonical encoded tokens rejected");
        Check(!GalleryContract.ValidAssetId("../final.jpg") && !GalleryContract.ValidAssetId("https://other.example/file.jpg"), "Arbitrary blob paths rejected");
        var tags = new Dictionary<string, string> { ["EventId"] = "anna-david", ["GuestKeyHash"] = hash, ["Kind"] = "Final" };
        var metadata = new Dictionary<string, string> { ["schemaversion"] = "1", ["contentsha256"] = new string('a', 64), ["originalindex"] = "0", ["expectedoriginalcount"] = "2", ["capturetimeutc"] = "2020-01-01T00:00:00Z" };
        var asset = GalleryContract.Parse(hash, "final", 120, "image/jpeg", "Cold", tags, metadata);
        Check(asset is not null, "Valid Cold upload contract is accepted");
        var gifAsset = GalleryContract.Parse(hash, "final", 120, "image/gif", "Cold", tags, metadata);
        Check(gifAsset is not null && gifAsset.BlobName.EndsWith("/final.gif"), "GIF sessions resolve their original animated final");
        Check(asset!.CapturedUtc!.StartsWith("2020"), "Old captures do not expire");
        var signingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Azure:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=" + Convert.ToBase64String(new byte[32]) + ";EndpointSuffix=core.windows.net",
            ["Azure:Container"] = "photos"
        }).Build();
        Uri signed = new AzurePhotoStore(signingConfiguration).DownloadUrl(asset, true);
        var parameters = QueryHelpers.ParseQuery(signed.Query);
        Check(parameters["sp"] == "r" && parameters["spr"] == "https" && parameters["sr"] == "b", "Real Azure signer grants only HTTPS reads of one blob");
        Check(DateTimeOffset.Parse(parameters["se"]!) < DateTimeOffset.UtcNow.AddMinutes(6) && signed.AbsolutePath.EndsWith("/final.jpg"), "Storage URLs are short-lived and file-specific");
        Check(!signed.AbsoluteUri.Contains(token) && parameters["rscd"].ToString().StartsWith("attachment"), "Signed downloads do not reveal the guest bearer token");
        Uri gifSigned = new AzurePhotoStore(signingConfiguration).DownloadUrl(gifAsset!, true);
        Check(gifSigned.AbsolutePath.EndsWith("/final.gif") && QueryHelpers.ParseQuery(gifSigned.Query)["rscd"].ToString().Contains("final.gif"), "GIF downloads preserve animated file names");
        Check(GalleryContract.Parse(new string('0', 64), "final", 120, "image/jpeg", "Cold", tags, metadata) is null, "Another guest's tags rejected");
        Check(GalleryContract.Parse(hash, "final", 120, "text/html", "Cold", tags, metadata) is null, "Non-image content rejected");
        Check(GalleryContract.Parse(hash, "final", 120, "image/jpeg", "Archive", tags, metadata) is null, "Archived files not represented as ready");
        metadata["expectedoriginalcount"] = "100000";
        Check(GalleryContract.Parse(hash, "final", 120, "image/jpeg", "Cold", tags, metadata) is null, "Malformed counts rejected");
        var store = new FakeStore(hash);
        using var factory = new Factory(store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var page = await client.GetAsync("/p/" + token);
        Check(page.StatusCode == HttpStatusCode.OK && (await page.Content.ReadAsStringAsync()).Contains("Yours to keep."), "Permanent QR route serves gallery");
        Check(page.Headers.CacheControl?.NoStore == true && page.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "Guest page privacy headers");
        Check((await client.GetAsync("/api/guest/42")).StatusCode == HttpStatusCode.NotFound, "Bad URL secret rejected");
        var waiting = await client.GetFromJsonAsync<JsonElement>("/api/guest/" + otherToken);
        Check(waiting.GetProperty("state").GetString() == "waiting" && waiting.GetProperty("originals").GetArrayLength() == 0, "Unknown guest sees no other photos");
        store.FinalAvailable = true;
        var partial = await client.GetFromJsonAsync<JsonElement>("/api/guest/" + token);
        Check(partial.GetProperty("state").GetString() == "uploading" && partial.GetProperty("expectedOriginals").GetInt32() == 2, "Final appears while originals pending");
        store.OriginalsAvailable = 2;
        var ready = await client.GetFromJsonAsync<JsonElement>("/api/guest/" + token);
        Check(ready.GetProperty("state").GetString() == "ready" && ready.GetProperty("originals").GetArrayLength() == 2, "Complete session includes only its originals");
        var download = await client.GetAsync($"/api/media/{token}/001.jpg?download=true");
        Check(download.StatusCode == HttpStatusCode.Redirect && download.Headers.Location!.AbsolutePath.Contains(hash), "Authorized media resolves to its own blob");
        Check((await client.GetAsync($"/api/media/{otherToken}/001.jpg")).StatusCode == HttpStatusCode.NotFound, "Other guest cannot read a known asset ID");
        Check((await client.GetAsync($"/api/media/{token}/999.jpg")).StatusCode == HttpStatusCode.NotFound, "Unknown asset is not signed");
        Check((await client.GetAsync($"/api/media/{token}/..%2F001.jpg")).StatusCode == HttpStatusCode.NotFound, "Encoded path traversal rejected");
        store.Fail = true;
        var failure = await client.GetAsync("/api/guest/" + token);
        string body = await failure.Content.ReadAsStringAsync();
        Check(failure.StatusCode == HttpStatusCode.ServiceUnavailable && !body.Contains("AccountKey"), "Storage failures never expose configuration");
        using var cleanFactory = new Factory(new FakeStore(hash));
        using var restarted = cleanFactory.CreateClient();
        Check((await restarted.GetAsync("/health")).StatusCode == HttpStatusCode.OK, "Fresh stateless instance starts without database");
        Console.WriteLine($"PASS: {count} gallery contract/access/HTTP checks.");
    }
    private sealed class Factory(FakeStore store) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/PicmeGallery.Web")));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => { services.RemoveAll<IPhotoStore>(); services.AddSingleton<IPhotoStore>(store); });
        }
    }
    private sealed class FakeStore(string permittedHash) : IPhotoStore
    {
        public bool FinalAvailable, Fail; public int OriginalsAvailable;
        private PhotoAsset Asset(string id) => new(id, GalleryContract.BlobName(permittedHash, id), "anna-david", 120, id == "final" ? 0 : int.Parse(id[..3]), 2, "2020-01-01T00:00:00Z");
        public Task<GuestPhotos> FindAsync(string hash, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("AccountKey=DO-NOT-EXPOSE");
            return Task.FromResult(hash != permittedHash || !FinalAvailable ? new GuestPhotos(null, []) :
                new GuestPhotos(Asset("final"), Enumerable.Range(1, OriginalsAvailable).Select(i => Asset($"{i:000}.jpg")).ToArray()));
        }
        public Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken token) => Task.FromResult(
            hash != permittedHash || !FinalAvailable || (id != "final" && !Enumerable.Range(1, OriginalsAvailable).Any(i => id == $"{i:000}.jpg")) ? null : Asset(id));
        public Uri DownloadUrl(PhotoAsset asset, bool attachment) => new("https://testaccount.blob.core.windows.net/photos/" + asset.BlobName + "?sp=r&sig=TEST");
    }
}
