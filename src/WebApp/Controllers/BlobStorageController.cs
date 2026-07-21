using Microsoft.AspNetCore.Mvc;
using WebApp.Services;

namespace WebApp.Controllers;

/// <summary>Exercises all three blob types end-to-end. See docs/BLOB_STORAGE.md.</summary>
[ApiController]
[Route("api/blobs")]
public class BlobStorageController : ControllerBase
{
    private const string ContainerName = "demo";
    private readonly IBlobStorageService _blobStorage;

    public BlobStorageController(IBlobStorageService blobStorage) => _blobStorage = blobStorage;

    [HttpPost("block/{blobName}")]
    public async Task<ActionResult> UploadBlockBlob(string blobName, IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        await _blobStorage.UploadBlockBlobAsync(ContainerName, blobName, stream, file.ContentType);
        return Ok(new { blobName, type = "Block", sizeBytes = file.Length });
    }

    [HttpPost("append/{blobName}")]
    public async Task<ActionResult> AppendLine(string blobName, [FromBody] string line)
    {
        await _blobStorage.AppendLineAsync(ContainerName, blobName, line);
        return Ok(new { blobName, type = "Append", appended = line });
    }

    [HttpPost("page/{blobName}")]
    public async Task<ActionResult> CreatePageBlob(string blobName, [FromQuery] long sizeInBytes = 512)
    {
        await _blobStorage.CreatePageBlobAsync(ContainerName, blobName, sizeInBytes);
        return Ok(new { blobName, type = "Page", sizeBytes = sizeInBytes });
    }

    [HttpPut("page/{blobName}")]
    public async Task<ActionResult> WritePage(string blobName, [FromQuery] long offset, [FromBody] byte[] data)
    {
        await _blobStorage.WritePageAsync(ContainerName, blobName, data, offset);
        return Ok(new { blobName, offset, bytesWritten = data.Length });
    }

    [HttpGet("{blobName}")]
    public async Task<ActionResult> Download(string blobName)
    {
        var stream = await _blobStorage.DownloadAsync(ContainerName, blobName);
        return stream is null ? NotFound() : File(stream, "application/octet-stream", blobName);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<string>>> List() =>
        Ok(await _blobStorage.ListBlobsAsync(ContainerName));
}
