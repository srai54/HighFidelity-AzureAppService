namespace WebApp.Services;

/// <summary>
/// Deliberately exposes all three Azure Blob types through distinct methods
/// instead of one generic "UploadAsync" — the whole point of this class is
/// to make the difference between them visible. See docs/BLOB_STORAGE.md.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Block blob — general-purpose file storage (images, documents, backups).</summary>
    Task UploadBlockBlobAsync(string containerName, string blobName, Stream content, string contentType);

    /// <summary>Append blob — write-once-read-many, append-only (audit logs, telemetry streams).</summary>
    Task AppendLineAsync(string containerName, string blobName, string line);

    /// <summary>Page blob — random read/write access in 512-byte pages (VHDs, sparse files).</summary>
    Task CreatePageBlobAsync(string containerName, string blobName, long sizeInBytes);

    Task WritePageAsync(string containerName, string blobName, byte[] data, long offset);

    Task<Stream?> DownloadAsync(string containerName, string blobName);

    Task<IReadOnlyList<string>> ListBlobsAsync(string containerName);
}
