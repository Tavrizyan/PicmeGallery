using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PicmeGallery.Web;

public static partial class GalleryContract
{
    public static string? TokenHash(string token)
    {
        if (!TokenFormat().IsMatch(token)) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32 || Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != token) return null;
            return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        }
        catch (FormatException) { return null; }
    }
    public static bool ValidAssetId(string id) => id == "final" || OriginalId().IsMatch(id);
    public static string BlobName(string hash, string id) => "sessions/" + hash + "/" + (id == "final" ? "final.jpg" : "originals/" + id);
    public static PhotoAsset? Parse(string hash, string id, long bytes, string? contentType,
        string? accessTier, IDictionary<string, string> tags, IDictionary<string, string> metadata)
    {
        bool final = id == "final";
        if (!ValidAssetId(id) || bytes <= 0 || accessTier != "Cold" ||
            (contentType != "image/jpeg" && contentType != "image/png" && !(final && contentType == "image/gif")) ||
            !tags.TryGetValue("GuestKeyHash", out var value) || value != hash ||
            !tags.TryGetValue("EventId", out var eventId) || !EventFormat().IsMatch(eventId) ||
            !tags.TryGetValue("Kind", out var kind) || kind != (final ? "Final" : "Original") ||
            !metadata.TryGetValue("schemaversion", out var schema) || schema != "1" ||
            !metadata.TryGetValue("contentsha256", out var checksum) || !HashFormat().IsMatch(checksum) ||
            !metadata.TryGetValue("expectedoriginalcount", out var countText) ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 100 ||
            !metadata.TryGetValue("originalindex", out var indexText) ||
            !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
            (final ? index != 0 : index < 1 || index > count || id[..3] != index.ToString("000", CultureInfo.InvariantCulture))) return null;
        metadata.TryGetValue("capturetimeutc", out string? captured);
        return new PhotoAsset(id, final && contentType == "image/gif" ? "sessions/" + hash + "/final.gif" : BlobName(hash, id), eventId, bytes, index, count, captured);
    }
    [GeneratedRegex("\\A[A-Za-z0-9_-]{43}\\z", RegexOptions.CultureInvariant)] private static partial Regex TokenFormat();
    [GeneratedRegex("\\A[a-z0-9][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant)] private static partial Regex EventFormat();
    [GeneratedRegex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)] private static partial Regex HashFormat();
    [GeneratedRegex("\\A[0-9]{3}\\.(?:jpg|jpeg|png)\\z", RegexOptions.CultureInvariant)] private static partial Regex OriginalId();
}

public sealed record PhotoAsset(string Id, string BlobName, string EventId, long Bytes, int Index, int ExpectedOriginals, string? CapturedUtc);
public sealed record GuestPhotos(PhotoAsset? Final, IReadOnlyList<PhotoAsset> Originals);
public interface IPhotoStore
{
    Task<GuestPhotos> FindAsync(string hash, CancellationToken token);
    Task<PhotoAsset?> GetAsync(string hash, string id, CancellationToken token);
    Uri DownloadUrl(PhotoAsset asset, bool attachment);
}
