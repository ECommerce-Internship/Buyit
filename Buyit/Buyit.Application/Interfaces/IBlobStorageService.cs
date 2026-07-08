using Microsoft.AspNetCore.Http;   // IFormFile lives here

namespace Buyit.Application.Interfaces;

/// <summary>
/// The contract for storing and removing files in an external blob store
/// (implemented by Azure Blob Storage). Callers depend on THIS interface, not
/// on Azure directly, so the storage provider can be swapped without touching
/// business code.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Uploads one file to the given container and returns its public URL.
    /// The blob is named "{productId}/{Guid}{extension}" so every product's
    /// images are grouped and never collide.
    /// </summary>
    /// <remarks>
    /// TB-42 note: the ticket listed (IFormFile, string containerName) but the
    /// required blob name needs productId, so it is added here (see guide §5).
    /// </remarks>
    Task<string> UploadAsync(IFormFile file, string containerName, int productId);

    /// <summary>
    /// Deletes the blob identified by its full public URL.
    /// Safe to call even if the blob no longer exists (DeleteIfExistsAsync).
    /// </summary>
    Task DeleteAsync(string blobUrl);
}