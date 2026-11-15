using System.Security.Cryptography;

namespace PicmeGallery.Web;

public sealed record EventRecord(string EventId, string Name, DateTimeOffset CreatedUtc, string? TokenHash, string? ProtectedToken);
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
