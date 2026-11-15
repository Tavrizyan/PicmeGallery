using System.Security.Cryptography;

namespace PicmeGallery.Web;

public sealed record EventRecord(string EventId, string Name, DateTimeOffset CreatedUtc, string? TokenHash, string? ProtectedToken,
    IReadOnlyList<string>? HiddenSessionIds = null)
{
    public bool IsHidden(string sessionId) => HiddenSessionIds?.Contains(sessionId, StringComparer.Ordinal) == true;
}
public sealed record StoredEvent(EventRecord Record, string Version);
public sealed record EventListPage(IReadOnlyList<StoredEvent> Events, string? Continuation);
public sealed record EventFinal(string SessionId, PhotoAsset Final);
public sealed record EventPhotoPage(IReadOnlyList<EventFinal> Sessions, string? Continuation);
public sealed class EventConflictException : Exception;

public interface IEventStore
{
    Task<EventListPage> ListAsync(string? continuation, CancellationToken cancellation);
    Task<StoredEvent?> GetAsync(string eventId, CancellationToken cancellation);
    Task<StoredEvent?> ResolveAsync(string tokenHash, CancellationToken cancellation);
    Task SaveAsync(EventRecord record, string? expectedVersion, CancellationToken cancellation);
}
public interface IEventPhotoStore
{
    Task<EventPhotoPage> ListFinalsAsync(string eventId, string? continuation, CancellationToken cancellation);
}
public static class EventAccess
{
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class SessionPages
{
    public static async Task<EventPhotoPage> ReadAsync(IEventPhotoStore store, string eventId, string? continuation,
        Func<EventFinal, bool> include, CancellationToken cancellation)
    {
        EventPhotoPage page;
        EventFinal[] sessions;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            page = await store.ListFinalsAsync(eventId, continuation, cancellation);
            sessions = page.Sessions.Where(include).ToArray();
            continuation = string.IsNullOrEmpty(page.Continuation) ? null : page.Continuation;
        } while (sessions.Length == 0 && continuation is not null);
        return new(sessions, continuation);
    }
}
