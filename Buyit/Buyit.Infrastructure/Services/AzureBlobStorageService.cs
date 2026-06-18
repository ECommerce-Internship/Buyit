using Azure.Storage.Blobs;            // BlobServiceClient, BlobContainerClient
using Azure.Storage.Blobs.Models;     // BlobHttpHeaders (to set content-type)
using Buyit.Application.Interfaces;   // the IBlobStorageService contract
using Microsoft.AspNetCore.Http;      // IFormFile
using Microsoft.Extensions.Logging;   // ILogger

namespace Buyit.Infrastructure.Services;

/// <summary>
/// Azure-Blob-Storage-backed implementation of IBlobStorageService.
/// Uses the shared (Singleton) BlobServiceClient to drill down to a container
/// and then a single blob for each upload/delete.
/// </summary>
public class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;          // the shared account-level client
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        ILogger<AzureBlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> UploadAsync(IFormFile file, string containerName, int productId)
    {
        // 1) Get a handle to the container (e.g. "product-images"). This does NOT
        //    hit the network yet — it just builds a client object pointing at it.
        BlobContainerClient container = _blobServiceClient.GetBlobContainerClient(containerName);

        // 2) Build the unique blob name: "{productId}/{Guid}{extension}".
        //    - Path.GetExtension keeps the leading dot, e.g. ".jpg".
        //    - ToLowerInvariant normalises ".JPG" -> ".jpg".
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        string blobName = $"{productId}/{Guid.NewGuid()}{extension}";

        // 3) Get a handle to that specific blob (still no network call).
        BlobClient blob = container.GetBlobClient(blobName);

        // 4) Open a read stream over the uploaded bytes and push them to Azure.
        //    'using' guarantees the stream is closed even if upload throws.
        //    BlobHttpHeaders sets the Content-Type so browsers DISPLAY the image
        //    instead of downloading it (see guide §3.5).
        await using (Stream stream = file.OpenReadStream())
        {
            var headers = new BlobHttpHeaders { ContentType = file.ContentType };
            await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers });
        }

        // 5) blob.Uri is the public URL, e.g.
        //    https://<account>.blob.core.windows.net/product-images/5/<guid>.jpg
        string url = blob.Uri.ToString();
        _logger.LogInformation("Uploaded blob {BlobName} for product {ProductId} -> {Url}",
            blobName, productId, url);
        return url;
    }

    public async Task DeleteAsync(string blobUrl)
    {
        // Defensive: nothing to do if there is no URL.
        if (string.IsNullOrWhiteSpace(blobUrl))
            return;

        // 1) Take the full URL apart to recover (a) the container name and
        //    (b) the blob name. A blob URL is always:
        //      https://<account>.blob.core.windows.net/<container>/<blobName...>
        //    new Uri(...).AbsolutePath gives "/product-images/5/<guid>.jpg".
        var uri = new Uri(blobUrl);
        string path = uri.AbsolutePath.TrimStart('/');           // "product-images/5/<guid>.jpg"
        int firstSlash = path.IndexOf('/');                       // split after the container name
        if (firstSlash < 0)
        {
            _logger.LogWarning("Could not parse blob URL {Url}; skipping delete.", blobUrl);
            return;
        }
        string containerName = path.Substring(0, firstSlash);     // "product-images"
        string blobName = path.Substring(firstSlash + 1);         // "5/<guid>.jpg"

        // 2) Drill down to the blob and delete it IF it exists.
        //    DeleteIfExistsAsync never throws when the blob is already gone,
        //    so calling delete twice (or on a missing file) is safe.
        BlobContainerClient container = _blobServiceClient.GetBlobContainerClient(containerName);
        BlobClient blob = container.GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync();

        _logger.LogInformation("Deleted blob {BlobName} from {Container}", blobName, containerName);
    }
}