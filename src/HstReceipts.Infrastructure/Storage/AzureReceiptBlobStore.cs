using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Storage;

public sealed class AzureReceiptBlobStore : IReceiptBlobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly BlobServiceClient? _client;
    private readonly BlobStorageOptions _options;
    private readonly ILogger<AzureReceiptBlobStore> _logger;

    public AzureReceiptBlobStore(
        IOptions<BlobStorageOptions> options,
        ILogger<AzureReceiptBlobStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.IsConfigured)
        {
            _client = new BlobServiceClient(_options.ConnectionString);
        }
    }

    public bool IsAvailable => _client is not null;

    public async Task UploadInboxAsync(
        Guid batchId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var container = client.GetBlobContainerClient(_options.InboxContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobName = $"{batchId:D}/{SanitizeFileName(fileName)}";
        var blob = container.GetBlobClient(blobName);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var headers = new BlobHttpHeaders
        {
            ContentType = GuessContentType(fileName)
        };
        var metadata = new Dictionary<string, string>
        {
            ["batchId"] = batchId.ToString("D"),
            ["originalFileName"] = fileName
        };

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = headers, Metadata = metadata },
            cancellationToken);

        _logger.LogInformation("Uploaded {File} to inbox blob {Blob}", fileName, blobName);
    }

    public async Task WriteManifestAsync(
        Guid batchId,
        int totalFiles,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var container = client.GetBlobContainerClient(_options.ResultsContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var manifest = new PipelineManifest
        {
            BatchId = batchId,
            TotalFiles = totalFiles,
            CompletedFiles = 0,
            FailedFiles = 0,
            Status = "processing",
            CreatedUtc = DateTime.UtcNow
        };

        var blob = container.GetBlobClient($"{batchId:D}/manifest.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await blob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(json)),
            overwrite: true,
            cancellationToken);
    }

    public async Task WriteFileResultAsync(
        Guid batchId,
        string sourceFileName,
        IReadOnlyList<ExtractedReceipt> rows,
        bool fileSucceeded,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var container = client.GetBlobContainerClient(_options.ResultsContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var safeName = SanitizeFileName(sourceFileName);
        var resultBlob = container.GetBlobClient($"{batchId:D}/{safeName}.result.json");
        var resultJson = JsonSerializer.Serialize(rows, JsonOptions);
        await resultBlob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(resultJson)),
            overwrite: true,
            cancellationToken);

        var manifestBlob = container.GetBlobClient($"{batchId:D}/manifest.json");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (!await manifestBlob.ExistsAsync(cancellationToken))
            {
                // Orphaned inbox blobs (Function started against old uploads) — create a 1-file manifest.
                var bootstrap = new PipelineManifest
                {
                    BatchId = batchId,
                    TotalFiles = 1,
                    CompletedFiles = 0,
                    FailedFiles = 0,
                    Status = "processing",
                    CreatedUtc = DateTime.UtcNow
                };
                var bootstrapJson = JsonSerializer.Serialize(bootstrap, JsonOptions);
                try
                {
                    await manifestBlob.UploadAsync(
                        new MemoryStream(Encoding.UTF8.GetBytes(bootstrapJson)),
                        overwrite: false,
                        cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "BlobAlreadyExists")
                {
                    // Another worker created it — retry the read/update loop.
                }
            }

            var download = await manifestBlob.DownloadContentAsync(cancellationToken);
            var etag = download.Value.Details.ETag;
            var manifest = JsonSerializer.Deserialize<PipelineManifest>(
                download.Value.Content.ToString(),
                JsonOptions) ?? new PipelineManifest { BatchId = batchId };

            if (fileSucceeded)
            {
                manifest.CompletedFiles++;
            }
            else
            {
                manifest.FailedFiles++;
            }

            if (manifest.CompletedFiles + manifest.FailedFiles >= manifest.TotalFiles && manifest.TotalFiles > 0)
            {
                manifest.Status = manifest.CompletedFiles > 0 ? "completed" : "failed";
                manifest.CompletedUtc = DateTime.UtcNow;
                if (manifest.Status == "failed")
                {
                    manifest.ErrorMessage ??= "All files failed Document Intelligence processing.";
                }
            }
            else
            {
                manifest.Status = "processing";
            }

            try
            {
                var updated = JsonSerializer.Serialize(manifest, JsonOptions);
                await manifestBlob.UploadAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes(updated)),
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfMatch = etag }
                    },
                    cancellationToken);
                _logger.LogInformation(
                    "Preview results for {File} stored in blob (batch {Batch}, ok={Ok}). Receipts SQL is not written until Export Excel and save.",
                    sourceFileName,
                    batchId,
                    fileSucceeded);
                return;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status is 412)
            {
                await Task.Delay(50 * (attempt + 1), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Could not update batch manifest for {batchId} after concurrent writes.");
    }

    public async Task<ReceiptPipelineBatchStatus> GetBatchStatusAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var results = client.GetBlobContainerClient(_options.ResultsContainer);
        var manifestBlob = results.GetBlobClient($"{batchId:D}/manifest.json");

        if (!await manifestBlob.ExistsAsync(cancellationToken))
        {
            return new ReceiptPipelineBatchStatus
            {
                BatchId = batchId,
                Status = "processing",
                TotalFiles = 0,
                ErrorMessage = "Batch manifest not found yet."
            };
        }

        var download = await manifestBlob.DownloadContentAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<PipelineManifest>(
            download.Value.Content.ToString(),
            JsonOptions) ?? new PipelineManifest { BatchId = batchId };

        var receipts = new List<ExtractedReceipt>();
        if (string.Equals(manifest.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || manifest.CompletedFiles + manifest.FailedFiles >= manifest.TotalFiles)
        {
            await foreach (var item in results.GetBlobsAsync(
                prefix: $"{batchId:D}/",
                cancellationToken: cancellationToken))
            {
                if (!item.Name.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var blob = results.GetBlobClient(item.Name);
                var content = await blob.DownloadContentAsync(cancellationToken);
                var rows = JsonSerializer.Deserialize<List<ExtractedReceipt>>(
                    content.Value.Content.ToString(),
                    JsonOptions);
                if (rows is { Count: > 0 })
                {
                    receipts.AddRange(rows);
                }
            }
        }

        var status = manifest.Status;
        if (manifest.FailedFiles > 0 && manifest.CompletedFiles + manifest.FailedFiles >= manifest.TotalFiles
            && manifest.CompletedFiles == 0)
        {
            status = "failed";
        }
        else if (manifest.CompletedFiles + manifest.FailedFiles >= manifest.TotalFiles && manifest.TotalFiles > 0)
        {
            status = "completed";
        }

        return new ReceiptPipelineBatchStatus
        {
            BatchId = batchId,
            Status = status,
            TotalFiles = manifest.TotalFiles,
            CompletedFiles = manifest.CompletedFiles,
            FailedFiles = manifest.FailedFiles,
            ErrorMessage = manifest.ErrorMessage,
            Receipts = receipts
        };
    }

    private BlobServiceClient RequireClient() =>
        _client ?? throw new InvalidOperationException("Blob storage is not configured.");

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "receipt.bin" : name;
    }

    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
}
