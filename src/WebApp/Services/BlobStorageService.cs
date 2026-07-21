using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace WebApp.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public BlobStorageService(BlobServiceClient blobServiceClient) => _blobServiceClient = blobServiceClient;

    public async Task UploadBlockBlobAsync(string containerName, string blobName, Stream content, string contentType)
    {
        var container = await GetContainerAsync(containerName);
        var blob = container.GetBlockBlobClient(blobName);

        // Block blobs upload as a set of blocks committed together — the SDK
        // handles chunking/committing for anything past the single-request
        // size threshold. This is the default choice for "just store this file."
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });
    }

    public async Task AppendLineAsync(string containerName, string blobName, string line)
    {
        var container = await GetContainerAsync(containerName);
        var blob = container.GetAppendBlobClient(blobName);

        if (!await blob.ExistsAsync())
            await blob.CreateAsync();

        // Append blobs only support adding blocks to the end — no editing
        // earlier content, no random access. Exactly the "keep appending log
        // lines" shape, and nothing else.
        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        using var stream = new MemoryStream(bytes);
        await blob.AppendBlockAsync(stream);
    }

    public async Task CreatePageBlobAsync(string containerName, string blobName, long sizeInBytes)
    {
        if (sizeInBytes % 512 != 0)
            throw new ArgumentException("Page blob size must be a multiple of 512 bytes.", nameof(sizeInBytes));

        var container = await GetContainerAsync(containerName);
        var blob = container.GetPageBlobClient(blobName);

        // Page blobs pre-allocate fixed, 512-byte-aligned storage that can be
        // written to at arbitrary offsets afterward — the shape a VHD or any
        // randomly-addressable virtual disk needs. Nothing about a typical
        // web app's file uploads needs this; it exists because Azure VMs'
        // OS/data disks are backed by page blobs under the hood.
        await blob.CreateAsync(sizeInBytes);
    }

    public async Task WritePageAsync(string containerName, string blobName, byte[] data, long offset)
    {
        if (offset % 512 != 0 || data.Length % 512 != 0)
            throw new ArgumentException("Page blob writes must start at a 512-byte-aligned offset and be a multiple of 512 bytes long.");

        var container = await GetContainerAsync(containerName);
        var blob = container.GetPageBlobClient(blobName);

        using var stream = new MemoryStream(data);
        await blob.UploadPagesAsync(stream, offset);
    }

    public async Task<Stream?> DownloadAsync(string containerName, string blobName)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadStreamingAsync();
        return response.Value.Content;
    }

    public async Task<IReadOnlyList<string>> ListBlobsAsync(string containerName)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync())
            return [];

        var names = new List<string>();
        await foreach (var blobItem in container.GetBlobsAsync())
            names.Add(blobItem.Name);
        return names;
    }

    private async Task<BlobContainerClient> GetContainerAsync(string containerName)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync();
        return container;
    }
}
