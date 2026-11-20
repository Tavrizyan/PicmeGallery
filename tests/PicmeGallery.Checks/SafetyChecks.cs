using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PicmeGallery.Web;

namespace PicmeGallery.Checks;

internal static class SafetyChecks
{
    private static int count;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
    public static async Task RunAsync()
    {
        var guard = new PaginationGuard(5);
        guard.BeforeRead(null, CancellationToken.None); guard.BeforeRead("a", CancellationToken.None); guard.BeforeRead("b", CancellationToken.None);
        try { guard.BeforeRead("a", CancellationToken.None); throw new Exception("Cycle was not rejected"); }
        catch (PaginationSafetyException error) { Check(error.Reason == PaginationStopReason.RepeatedContinuation, "Cycle detection catches non-adjacent repeated tokens"); }
        var cancelledGuard = new PaginationGuard(5);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { cancelledGuard.BeforeRead(null, cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "A canceled request cannot start another storage page read"); }
        }
        var store = new FaultStore();
        using var factory = new Factory(store);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(8);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/admin/session");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.GetProperty("csrfToken").GetString());
        Check((await client.PostAsJsonAsync("/api/admin/login", new { username = "safety-admin", password = "test-safety-password" })).StatusCode == HttpStatusCode.OK, "Safety HTTP checks use authenticated admin access");
        foreach (var scenario in new[] { ("repeat", 2), ("cycle", 3), ("unique", 3) })
        {
            store.Reset(scenario.Item1);
            var response = await client.GetAsync("/api/admin/events");
            string body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null,
                "Faulty registry pagination returns a retryable safety error");
            Check(store.EventReads == scenario.Item2, "Registry guard stops before another storage read: " + scenario.Item1);
            Check(!body.Contains("SECRET") && !body.Contains("AccountKey"), "Safety errors do not expose continuation tokens or configuration");
        }
        foreach (string ending in new[] { "empty", "null", "finite" })
        {
            store.Reset(ending);
            var events = await client.GetFromJsonAsync<JsonElement>("/api/admin/events");
            Check(events.GetProperty("events").GetArrayLength() == (ending == "finite" ? 3 : 1) && store.EventReads == (ending == "finite" ? 3 : 1),
                "Valid registry traversal completes, including exactly the maximum pages: " + ending);
        }
        string ownerApi = "/api/event/" + store.OwnerToken;
        foreach (string route in new[] { ownerApi, "/api/admin/events/test-event/sessions?filter=hidden" })
        {
            store.Reset("repeat");
            Check((await client.GetAsync(route)).StatusCode == HttpStatusCode.ServiceUnavailable && store.PhotoReads == 2,
                "Filtered session traversal detects repeated tokens without more reads");
            store.Reset("unique");
            Check((await client.GetAsync(route)).StatusCode == HttpStatusCode.ServiceUnavailable && store.PhotoReads == 3,
                "Filtered session traversal enforces its page limit");
        }
        foreach (string scenario in new[] { "repeat", "unique" })
        {
            store.Reset(scenario); bool interrupted = false;
            try { await client.GetAsync(ownerApi + "/download"); }
            catch (HttpRequestException) { interrupted = true; }
            Check(interrupted && store.PhotoReads == (scenario == "repeat" ? 2 : 3), "Archive traversal aborts on a loop/page limit instead of returning a successful partial ZIP");
        }
        store.Reset("slow");
        var timeout = await client.GetAsync("/api/admin/events");
        Check(timeout.StatusCode == HttpStatusCode.GatewayTimeout && store.CancellationObserved.Task.IsCompletedSuccessfully && store.EventReads == 1,
            "Application API deadline cancels a slow storage request without relying on browser cancellation");
        store.Reset("slow");
        var photoTimeout = await client.GetAsync(ownerApi);
        Check(photoTimeout.StatusCode == HttpStatusCode.GatewayTimeout && store.CancellationObserved.Task.IsCompletedSuccessfully && store.PhotoReads == 1,
            "Owner session storage uses the same application deadline");
        store.Reset("slow");
        using (var cancellation = new CancellationTokenSource())
        {
            var request = client.GetAsync("/api/admin/events", cancellation.Token);
            await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancellation.Cancel();
            try { await request; throw new Exception("Client cancellation ignored"); }
            catch (OperationCanceledException) { }
            await store.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(store.EventReads == 1, "Client disconnect cancels storage and does not start another page");
        }
        store.Reset("slow");
        var watch = Stopwatch.StartNew(); bool archiveStopped = false;
        try { await client.GetAsync(ownerApi + "/download/"); }
        catch (HttpRequestException) { archiveStopped = true; }
        Check(archiveStopped && store.CancellationObserved.Task.IsCompletedSuccessfully && watch.Elapsed >= TimeSpan.FromMilliseconds(1500),
            "Archive uses its separate finite deadline, including a trailing-slash route");
        Check((await client.GetAsync("/health")).StatusCode == HttpStatusCode.OK, "Service remains healthy after safety stops");
        Console.WriteLine($"PASS: {count} pagination/deadline/cancellation safety checks.");
    }
    private sealed class Factory(FaultStore store) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/PicmeGallery.Web")));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Username"] = "safety-admin", ["Admin:Password"] = "test-safety-password",
                ["StorageSafety:MaxPagesPerRequest"] = "3", ["StorageSafety:ApiTimeoutSeconds"] = "1", ["StorageSafety:ArchiveTimeoutSeconds"] = "2"
            }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.Remove(services.Single(service => service.ImplementationType == typeof(SessionCleanup)));
                services.RemoveAll<IEventStore>(); services.RemoveAll<IEventPhotoStore>(); services.RemoveAll<IPhotoStore>();
                services.AddSingleton<IEventStore>(store); services.AddSingleton<IEventPhotoStore>(store); services.AddSingleton<IPhotoStore>(store);
            });
        }
    }
    private sealed class FaultStore : IEventStore, IEventPhotoStore, IPhotoStore
    {
        public string OwnerToken { get; } = EventAccess.NewToken();
        public int EventReads, PhotoReads;
        public string Mode = "empty";
        public TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset(string mode)
        {
            Mode = mode; EventReads = PhotoReads = 0;
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private string? Next(int reads) => Mode switch
        {
            "repeat" => "SECRET-repeat", "cycle" => reads % 2 == 1 ? "SECRET-a" : "SECRET-b",
            "unique" => "SECRET-" + reads, "finite" => reads < 3 ? "page-" + reads : null,
            "null" => null, _ => ""
        };
        private async Task Delay(CancellationToken cancellation)
        {
            ReadStarted.TrySetResult();
            if (Mode != "slow") return;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
            catch (OperationCanceledException) { CancellationObserved.TrySetResult(); throw; }
        }
        public async Task<EventListPage> ListAsync(string? continuation, CancellationToken cancellation)
        {
            EventReads++; await Delay(cancellation);
            return new([new(new("event-" + EventReads, "Event", DateTimeOffset.UtcNow, null, null), "1")], Next(EventReads));
        }
        public Task<StoredEvent?> GetAsync(string eventId, CancellationToken cancellation) => Task.FromResult<StoredEvent?>(
            new(new("test-event", "Safety event", DateTimeOffset.UtcNow, GalleryContract.TokenHash(OwnerToken), null), "1"));
        public Task<StoredEvent?> ResolveAsync(string hash, CancellationToken cancellation) => GetAsync("test-event", cancellation);
        public Task SaveAsync(EventRecord record, string? expectedVersion, bool writeAccessPointer, CancellationToken cancellation) => throw new NotSupportedException();
        public async Task<EventPhotoPage> ListFinalsAsync(string eventId, string? continuation, CancellationToken cancellation)
        {
            PhotoReads++; await Delay(cancellation); return new([], Next(PhotoReads));
        }
        public Task<GuestPhotos> FindAsync(string hash, CancellationToken cancellation) => Task.FromResult(new GuestPhotos(null, []));
        public Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken cancellation) => Task.FromResult<PhotoAsset?>(null);
        public Uri DownloadUrl(PhotoAsset asset, bool attachment) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(PhotoAsset asset, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
