using Azure;
using PicmeGallery.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddSingleton<IPhotoStore, AzurePhotoStore>();
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' https:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        if (!context.Response.HasStarted) context.Response.StatusCode = 499;
    }
    catch (Exception error)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { message = "The photo service is temporarily unavailable. Please try again shortly." });
        // Do not log exception text/URLs: requests contain bearer secrets.
        app.Logger.LogWarning("Photo storage unavailable ({FailureType}).", error.GetType().Name);
    }
});
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { service = "PicmeGallery", status = "running" }));
app.MapGet("/api/guest/{token}", async (string token, IPhotoStore store, CancellationToken cancellation) =>
{
    string? hash = GalleryContract.TokenHash(token);
    if (hash is null) return Results.NotFound();
    var photos = await store.FindAsync(hash, cancellation);
    object Describe(PhotoAsset p) => new
    {
        id = p.Id, bytes = p.Bytes, capturedUtc = p.CapturedUtc,
        previewUrl = $"/api/media/{token}/{p.Id}", downloadUrl = $"/api/media/{token}/{p.Id}?download=true"
    };
    int expected = photos.Final?.ExpectedOriginals ?? 0;
    return Results.Ok(new
    {
        state = photos.Final is null ? "waiting" : photos.Originals.Count >= expected ? "ready" : "uploading",
        expectedOriginals = expected, uploadedOriginals = photos.Originals.Count,
        final = photos.Final is null ? null : Describe(photos.Final), originals = photos.Originals.Select(Describe).ToArray()
    });
});
app.MapGet("/api/media/{token}/{id}", async (string token, string id, bool? download, IPhotoStore store, CancellationToken cancellation) =>
{
    string? hash = GalleryContract.TokenHash(token);
    if (hash is null || !GalleryContract.ValidAssetId(id)) return Results.NotFound();
    var photo = await store.GetAsync(hash, id, cancellation);
    return photo is null ? Results.NotFound() : Results.Redirect(store.DownloadUrl(photo, download == true).AbsoluteUri);
});
app.MapGet("/p/{token}", (string token, IWebHostEnvironment environment) => GalleryContract.TokenHash(token) is null ?
    Results.NotFound() : Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html"));
app.MapGet("/", (IWebHostEnvironment environment) => Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html"));
app.Run();

public partial class Program;
