using Azure.Storage.Blobs;

namespace PicmeGallery.Web;

internal static class BlobStorage
{
    public static BlobContainerClient Container(IConfiguration configuration)
    {
        string? connection = configuration["Azure:ConnectionString"], name = configuration["Azure:Container"];
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Azure storage has not been configured.");
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 1; options.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);
        var container = new BlobServiceClient(connection, options).GetBlobContainerClient(name);
        if (container.Uri.Scheme != "https") throw new InvalidOperationException("Azure requires HTTPS.");
        return container;
    }
}
