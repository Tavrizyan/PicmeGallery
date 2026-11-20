using Microsoft.Extensions.Options;

namespace PicmeGallery.Web;

public sealed class SessionCleanup(IEventStore events, ISessionPhotoDeletion photos,
    IOptions<StorageSafetyOptions> safety, ILogger<SessionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            try { await RunOnceAsync(DateTimeOffset.UtcNow, deadline.Token); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // Retry on the next sweep; never log storage URLs or bearer secrets.
                logger.LogWarning("Session cleanup incomplete ({FailureType}); will retry.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        string? cursor = null;
        var guard = new PaginationGuard(safety.Value.MaxPagesPerRequest);
        do
        {
            guard.BeforeRead(cursor, cancellation);
            var page = await events.ListAsync(cursor, cancellation);
            foreach (var entry in page.Events)
            {
                var expired = (entry.Record.DeletedSessions ?? new Dictionary<string, DeletedSession>())
                    .Where(pair => !pair.Value.CanRestore(now)).ToArray();
                if (expired.Length == 0) continue;
                // Claim all newly expired sessions in one conditional write for this event.
                if (expired.Any(pair => !pair.Value.Purging))
                {
                    var states = new Dictionary<string, DeletedSession>(entry.Record.DeletedSessions!);
                    foreach (var pair in expired) states[pair.Key] = pair.Value with { Purging = true };
                    try { await events.SaveAsync(entry.Record with { DeletedSessions = states }, entry.Version, false, cancellation); }
                    catch (EventConflictException) { continue; } // Retry from fresh state on the next sweep.
                }
                foreach (var pair in expired)
                    await photos.DeleteAsync(pair.Key, cancellation);
            }
            cursor = string.IsNullOrEmpty(page.Continuation) ? null : page.Continuation;
        } while (cursor is not null);
    }
}
