using Microsoft.Extensions.Options;

namespace PicmeGallery.Web;

public sealed class StorageSafetyOptions
{
    public int MaxPagesPerRequest { get; set; } = 100;
    public int ApiTimeoutSeconds { get; set; } = 30;
    public int ArchiveTimeoutSeconds { get; set; } = 600;
}

public enum PaginationStopReason { RepeatedContinuation, PageLimit }
public sealed class PaginationSafetyException(PaginationStopReason reason) : Exception("Storage traversal stopped by a safety limit.")
{
    public PaginationStopReason Reason { get; } = reason;
}

public sealed class PaginationGuard(int maxPages)
{
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private int pages;

    public void BeforeRead(string? continuation, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (pages >= maxPages) throw new PaginationSafetyException(PaginationStopReason.PageLimit);
        // Treat null and empty identically, including if a broken loop tries to restart the first page.
        if (!seen.Add(continuation ?? "")) throw new PaginationSafetyException(PaginationStopReason.RepeatedContinuation);
        pages++;
    }
}

// An application deadline is independent of the browser/Cloudflare keeping an upstream request open.
public sealed class StorageDeadlineMiddleware(RequestDelegate next, IOptions<StorageSafetyOptions> options, ILogger<StorageDeadlineMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
        bool archive = context.Request.Path.StartsWithSegments("/api/event") && context.Request.Path.Value?.TrimEnd('/').EndsWith("/download", StringComparison.OrdinalIgnoreCase) == true;
        int seconds = archive ? options.Value.ArchiveTimeoutSeconds : options.Value.ApiTimeoutSeconds;
        var original = context.RequestAborted;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(original, deadline.Token);
        context.RequestAborted = linked.Token;
        try { await next(context); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !original.IsCancellationRequested)
        {
            // Log only the category, never bearer paths, SAS URLs, credentials, or continuation tokens.
            logger.LogWarning("Storage request reached its application deadline ({Category}).", archive ? "archive" : "api");
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new { message = "This request took too long and was stopped. Please try again later." }, original);
        }
        finally { context.RequestAborted = original; }
    }
}
