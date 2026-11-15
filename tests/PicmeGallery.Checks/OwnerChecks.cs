using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PicmeGallery.Web;

namespace PicmeGallery.Checks;

internal static class OwnerChecks
{
    private static int count;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
    public static async Task RunAsync()
    {
        var store = new MemoryEvents(); var photos = new MemoryPhotos();
        using var factory = new Factory(store, photos);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Check((await client.GetAsync("/admin")).StatusCode == HttpStatusCode.Redirect, "Admin page redirects to login");
        Check((await client.GetAsync("/api/admin/events")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous event listing rejected");
        var sessionResponse = await client.GetAsync("/api/admin/session");
        var cookie = sessionResponse.Headers.GetValues("Set-Cookie").Single();
        Check(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "CSRF cookie is secure, HttpOnly, SameSite Strict");
        var anonymous = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Check(!anonymous.GetProperty("authenticated").GetBoolean(), "Anonymous session response");
        Check((await client.PostAsJsonAsync("/api/admin/login", new { username = "test-admin", password = "test-password-only" })).StatusCode == HttpStatusCode.BadRequest, "Login requires CSRF protection");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", anonymous.GetProperty("csrfToken").GetString());
        Check((await client.PostAsJsonAsync("/api/admin/login", new { username = "test-admin", password = "wrong" })).StatusCode == HttpStatusCode.Unauthorized, "Incorrect password rejected");
        var login = await client.PostAsJsonAsync("/api/admin/login", new { username = "test-admin", password = "test-password-only" });
        Check(login.StatusCode == HttpStatusCode.OK, "Configured credentials sign in");
        string authCookie = login.Headers.GetValues("Set-Cookie").Single();
        Check(authCookie.StartsWith("__Host-Picme.Admin=") && authCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase), "Admin authentication uses a secure HttpOnly host cookie");
        var authenticated = await client.GetFromJsonAsync<JsonElement>("/api/admin/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", authenticated.GetProperty("csrfToken").GetString());
        Check(authenticated.GetProperty("authenticated").GetBoolean(), "Authentication cookie is accepted");
        client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        Check((await client.PostAsJsonAsync("/api/admin/events", new { eventId = "test-event", name = "Test" })).StatusCode == HttpStatusCode.BadRequest, "Authenticated event mutations still require CSRF protection");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", authenticated.GetProperty("csrfToken").GetString());
        Check((await client.GetAsync("/admin")).StatusCode == HttpStatusCode.OK, "Authenticated admin page accessible");
        Check((await client.PostAsJsonAsync("/api/admin/events", new { eventId = "not valid!", name = "Test" })).StatusCode == HttpStatusCode.BadRequest, "Invalid event ID rejected");
        var created = await client.PostAsJsonAsync("/api/admin/events", new { eventId = "test-event", name = "Test celebration" });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        string path = createdBody.GetProperty("ownerPath").GetString()!, token = path.Split('/').Last();
        Check(created.StatusCode == HttpStatusCode.OK && GalleryContract.TokenHash(token) is not null, "Admin creates an unpredictable owner link");
        Check(store.Values["test-event"].Record.ProtectedToken != token && store.Values["test-event"].Record.TokenHash == GalleryContract.TokenHash(token), "Owner token is stored encrypted and hashed");
        Check((await client.PostAsJsonAsync("/api/admin/events", new { eventId = "test-event", name = "Duplicate" })).StatusCode == HttpStatusCode.Conflict, "Duplicate Event IDs are not overwritten");
        var listing = await client.GetFromJsonAsync<JsonElement>("/api/admin/events");
        var record = listing.GetProperty("events")[0];
        Check(record.GetProperty("ownerPath").GetString() == path && record.GetProperty("name").GetString() == "Test celebration", "Admin can recover and copy existing link");
        Check((await client.GetAsync("/api/admin/events?cursor=tampered")).StatusCode == HttpStatusCode.BadRequest, "Tampered admin cursor rejected");
        for (int i = 0; i < 35; i++)
        {
            string id = $"managed-{i:00}";
            store.Values[id] = new(new(id, id, DateTimeOffset.Parse("2024-01-01T00:00:00Z").AddDays(i / 2), null, null), "fixture-" + i);
        }
        var catalogRows = new List<JsonElement>(); string? catalogCursor = null; int catalogPages = 0;
        do
        {
            var catalogPage = await client.GetFromJsonAsync<JsonElement>("/api/admin/events" + (catalogCursor is null ? "" : "?cursor=" + Uri.EscapeDataString(catalogCursor)));
            catalogRows.AddRange(catalogPage.GetProperty("events").EnumerateArray());
            catalogCursor = catalogPage.GetProperty("nextCursor").GetString(); catalogPages++;
        } while (catalogCursor is not null);
        Check(catalogPages == 2 && catalogRows.Count == 36 && catalogRows.Select(row => row.GetProperty("eventId").GetString()).Distinct().Count() == 36, "Admin catalog reads all managed event pages without duplicates");
        Check(catalogRows[0].GetProperty("eventId").GetString() == "test-event" && catalogRows[0].GetProperty("name").GetString() == "Test celebration", "Managed event creation dates determine ordering");
        Check(catalogRows.Zip(catalogRows.Skip(1)).All(pair => pair.First.GetProperty("createdUtc").GetDateTimeOffset() >= pair.Second.GetProperty("createdUtc").GetDateTimeOffset()), "Event creation dates are descending across page boundaries");
        Check(catalogRows[1].GetProperty("eventId").GetString() == "managed-34" && catalogRows[2].GetProperty("eventId").GetString() == "managed-32" && catalogRows[3].GetProperty("eventId").GetString() == "managed-33", "Equal-date events have stable Event ID ordering");
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        string api = "/api/event/" + token;
        var first = await owner.GetFromJsonAsync<JsonElement>(api);
        Check(first.GetProperty("name").GetString() == "Test celebration" && first.GetProperty("sessions").GetArrayLength() == 1, "Owner sees paged event sessions without login");
        Check(!first.GetProperty("sessions")[0].TryGetProperty("originals", out _), "Owner grid descriptions contain only finals");
        string nextCursor = first.GetProperty("nextCursor").GetString()!;
        var second = await owner.GetFromJsonAsync<JsonElement>(api + "?cursor=" + Uri.EscapeDataString(nextCursor));
        Check(second.GetProperty("sessions")[0].GetProperty("sessionId").GetString() == MemoryPhotos.Second, "Owner continuation loads additional sessions");
        Check((await owner.GetAsync(api + "?cursor=forged")).StatusCode == HttpStatusCode.BadRequest, "Tampered owner cursor rejected");
        Check((await owner.GetAsync("/api/event/" + EventAccess.NewToken())).StatusCode == HttpStatusCode.NotFound, "Unknown owner token reveals no event");
        Check((await owner.GetAsync("/api/guest/" + token)).StatusCode == HttpStatusCode.OK, "Owner token is not a session token and sees guest waiting state");
        var session = await owner.GetFromJsonAsync<JsonElement>($"{api}/sessions/{MemoryPhotos.First}");
        Check(session.GetProperty("originals").GetArrayLength() == 2 && session.GetProperty("state").GetString() == "ready", "Owner session includes originals");
        Check((await owner.GetAsync($"{api}/sessions/{MemoryPhotos.Other}")).StatusCode == HttpStatusCode.NotFound, "Owner cannot view a different event session");
        Check((await owner.GetAsync($"{api}/media/{MemoryPhotos.Other}/final")).StatusCode == HttpStatusCode.NotFound, "Owner cannot download another event final");
        Check((await owner.GetAsync($"{api}/media/{MemoryPhotos.First}/003.jpg")).StatusCode == HttpStatusCode.NotFound, "Owner cannot fetch missing assets");
        Check((await owner.GetAsync($"{api}/media/{MemoryPhotos.First}/002.png?file=true")).Content.Headers.ContentType?.MediaType == "image/png", "Owner sharing streams PNG content type");
        var final = await owner.GetAsync($"{api}/media/{MemoryPhotos.First}/final?download=true");
        Check(final.StatusCode == HttpStatusCode.Redirect, "Owner final download redirects to scoped media");
        var zipResponse = await owner.GetAsync(api + "/download");
        Check(zipResponse.StatusCode == HttpStatusCode.OK && zipResponse.Content.Headers.ContentType?.MediaType == "application/zip" && zipResponse.Headers.CacheControl?.NoStore == true, "ZIP is private and downloadable");
        using var zipBytes = new MemoryStream(await zipResponse.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(zipBytes);
        Check(archive.Entries.Count == 5 && archive.Entries.Any(entry => entry.FullName == MemoryPhotos.Second + "/picme-final.gif"), "ZIP traverses all pages and includes originals and GIF finals");
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Check((await reader.ReadToEndAsync()).StartsWith("original-bytes:"), "ZIP preserves original bytes");
            Check(!entry.FullName.Contains(MemoryPhotos.Other), "ZIP excludes other events");
        }
        photos.FailReads = true;
        bool interrupted = false;
        try { await owner.GetAsync(api + "/download"); }
        catch (HttpRequestException) { interrupted = true; }
        finally { photos.FailReads = false; }
        Check(interrupted, "Failed photo transfer aborts the ZIP instead of returning an incomplete success");
        string version = record.GetProperty("version").GetString()!;
        Check((await client.PostAsJsonAsync("/api/admin/events/test-event/replace", new { version = "stale" })).StatusCode == HttpStatusCode.Conflict, "Stale admin mutation cannot replace a link");
        var replaced = await client.PostAsJsonAsync("/api/admin/events/test-event/replace", new { version });
        string newToken = (await replaced.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ownerPath").GetString()!.Split('/').Last();
        Check(newToken != token && (await owner.GetAsync(api)).StatusCode == HttpStatusCode.NotFound, "Replacing link immediately rejects old event token");
        Check((await owner.GetAsync(api + $"/media/{MemoryPhotos.First}/final")).StatusCode == HttpStatusCode.NotFound &&
            (await owner.GetAsync(api + "/download")).StatusCode == HttpStatusCode.NotFound, "Replaced link also loses media and ZIP access");
        Check((await owner.GetAsync("/api/event/" + newToken)).StatusCode == HttpStatusCode.OK, "Replacement link works");
        string currentVersion = store.Values["test-event"].Version;
        Check((await client.PostAsJsonAsync("/api/admin/events/test-event/revoke", new { version = currentVersion })).StatusCode == HttpStatusCode.OK &&
            (await owner.GetAsync("/api/event/" + newToken)).StatusCode == HttpStatusCode.NotFound, "Revocation removes event access");
        Check((await owner.GetAsync("/api/admin/events")).StatusCode == HttpStatusCode.Unauthorized, "Owner still cannot administer events");
        Check((await client.PostAsJsonAsync("/api/admin/logout", new { })).StatusCode == HttpStatusCode.OK &&
            (await client.GetAsync("/api/admin/events")).StatusCode == HttpStatusCode.Unauthorized, "Logout removes admin access");
        var freshSession = await client.GetFromJsonAsync<JsonElement>("/api/admin/session");
        client.DefaultRequestHeaders.Remove("X-CSRF-Token"); client.DefaultRequestHeaders.Add("X-CSRF-Token", freshSession.GetProperty("csrfToken").GetString());
        Check((await client.PostAsJsonAsync("/api/admin/login", new { username = "test-admin", password = "test-password-only" })).StatusCode == HttpStatusCode.OK, "Admin can sign in after logout");
        factory.Services.GetRequiredService<IConfiguration>()["Admin:Password"] = "updated-test-password";
        Check((await client.GetAsync("/api/admin/events")).StatusCode == HttpStatusCode.Unauthorized, "Configured password changes invalidate existing admin cookies");
        using var missingFactory = new Factory(store, photos, configured: false);
        using var missingClient = missingFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var missingSession = await missingClient.GetFromJsonAsync<JsonElement>("/api/admin/session");
        missingClient.DefaultRequestHeaders.Add("X-CSRF-Token", missingSession.GetProperty("csrfToken").GetString());
        Check((await missingClient.PostAsJsonAsync("/api/admin/login", new { username = "", password = "" })).StatusCode == HttpStatusCode.ServiceUnavailable, "Missing configuration never enables a default login");
        for (int i = 0; i < 10; i++) await missingClient.PostAsJsonAsync("/api/admin/login", new { username = "wrong", password = "wrong" });
        Check((await missingClient.PostAsJsonAsync("/api/admin/login", new { username = "wrong", password = "wrong" })).StatusCode == HttpStatusCode.TooManyRequests, "Login attempts are rate limited");
        Console.WriteLine($"PASS: {count} admin/event/access/ZIP checks.");
    }
    private sealed class Factory(MemoryEvents events, MemoryPhotos photos, bool configured = true) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/PicmeGallery.Web")));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Admin:Username"] = configured ? "test-admin" : "", ["Admin:Password"] = configured ? "test-password-only" : "" }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventStore>(); services.RemoveAll<IEventPhotoStore>(); services.RemoveAll<IPhotoStore>();
                services.AddSingleton<IEventStore>(events); services.AddSingleton<IEventPhotoStore>(photos); services.AddSingleton<IPhotoStore>(photos);
            });
        }
    }
    private sealed class MemoryEvents : IEventStore
    {
        public readonly Dictionary<string, StoredEvent> Values = [];
        private int revision;
        public Task<EventListPage> ListAsync(string? continuation, CancellationToken cancellation)
        {
            int offset = continuation is null ? 0 : int.Parse(continuation);
            var entries = Values.Values.Skip(offset).Take(12).ToArray();
            return Task.FromResult(new EventListPage(entries, offset + entries.Length < Values.Count ? (offset + entries.Length).ToString() : null));
        }
        public Task<StoredEvent?> GetAsync(string eventId, CancellationToken cancellation) => Task.FromResult(Values.GetValueOrDefault(eventId));
        public Task<StoredEvent?> ResolveAsync(string hash, CancellationToken cancellation) => Task.FromResult(Values.Values.FirstOrDefault(item => item.Record.TokenHash == hash));
        public Task SaveAsync(EventRecord record, string? expectedVersion, CancellationToken cancellation)
        {
            if (Values.GetValueOrDefault(record.EventId)?.Version != expectedVersion) throw new EventConflictException();
            Values[record.EventId] = new(record, (++revision).ToString()); return Task.CompletedTask;
        }
    }
    private sealed class MemoryPhotos : IPhotoStore, IEventPhotoStore
    {
        public bool FailReads;
        public static readonly string First = new('a', 64), Second = new('b', 64), Other = new('c', 64);
        private static PhotoAsset Asset(string hash, string id) => new(id,
            hash == Second && id == "final" ? $"sessions/{hash}/final.gif" : GalleryContract.BlobName(hash, id),
            hash == Other ? "another-event" : "test-event", 120, id == "final" ? 0 : int.Parse(id[..3]), hash == First ? 2 : 1, "2026-01-01T18:00:00Z");
        public Task<GuestPhotos> FindAsync(string hash, CancellationToken cancellation) => Task.FromResult(hash == First
            ? new GuestPhotos(Asset(hash, "final"), [Asset(hash, "001.jpg"), Asset(hash, "002.png")]) : hash == Second || hash == Other
                ? new GuestPhotos(Asset(hash, "final"), [Asset(hash, "001.jpg")]) : new GuestPhotos(null, []));
        public async Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken cancellation)
        {
            var gallery = await FindAsync(hash, cancellation); return id == "final" ? gallery.Final : gallery.Originals.FirstOrDefault(photo => photo.Id == id);
        }
        public Task<EventPhotoPage> ListFinalsAsync(string eventId, string? continuation, CancellationToken cancellation) => Task.FromResult(eventId != "test-event"
            ? new EventPhotoPage([], null) : continuation is null ? new EventPhotoPage([new(First, Asset(First, "final"))], "second-page") : new EventPhotoPage([new(Second, Asset(Second, "final"))], null));
        public Uri DownloadUrl(PhotoAsset photo, bool attachment) => new("https://example.blob.core.windows.net/photos/" + photo.BlobName);
        public Task<Stream> OpenReadAsync(PhotoAsset photo, CancellationToken cancellation)
        {
            if (FailReads && photo.BlobName.Contains(Second)) throw new IOException("Simulated transfer failure");
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("original-bytes:" + photo.BlobName)));
        }
    }
}
