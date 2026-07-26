using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HstReceipts.Functions;

public sealed class ProcessReceiptBlobFunction
{
    private readonly IDocumentReceiptAnalyzer _analyzer;
    private readonly IReceiptBlobStore _blobStore;
    private readonly ILogger<ProcessReceiptBlobFunction> _logger;

    public ProcessReceiptBlobFunction(
        IDocumentReceiptAnalyzer analyzer,
        IReceiptBlobStore blobStore,
        ILogger<ProcessReceiptBlobFunction> logger)
    {
        _analyzer = analyzer;
        _blobStore = blobStore;
        _logger = logger;
    }

    [Function(nameof(ProcessReceiptBlob))]
    public async Task ProcessReceiptBlob(
        [BlobTrigger("%InboxContainer%/{batchId}/{fileName}", Connection = "AzureWebJobsStorage")]
        Stream blobStream,
        string batchId,
        string fileName,
        FunctionContext context)
    {
        if (!Guid.TryParse(batchId, out var batchGuid))
        {
            _logger.LogWarning("Ignoring blob with invalid batch id folder: {BatchId}/{File}", batchId, fileName);
            return;
        }

        // Skip non-receipt blobs (e.g. accidental uploads) and avoid re-processing noise.
        if (string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Ignoring non-receipt blob {Batch}/{File}", batchId, fileName);
            return;
        }

        IReadOnlyList<ExtractedReceipt> rows;
        var ok = true;
        try
        {
            if (!_analyzer.IsAvailable)
            {
                throw new InvalidOperationException(
                    "Document Intelligence is not configured (DocumentIntelligence__Endpoint / ApiKey).");
            }

            rows = await _analyzer.AnalyzeAsync(blobStream, fileName, context.CancellationToken);
        }
        catch (Exception ex)
        {
            ok = false;
            _logger.LogError(ex, "Document Intelligence failed for {Batch}/{File}", batchId, fileName);
            rows =
            [
                new ExtractedReceipt
                {
                    ReceiptName = fileName,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Warnings = { $"Pipeline processing failed: {ex.Message}" }
                }
            ];
        }

        // Preview only — blob results, not the Receipts table (that waits for Export Excel and save).
        await _blobStore.WriteFileResultAsync(batchGuid, fileName, rows, ok, context.CancellationToken);
        _logger.LogInformation(
            "Preview: wrote {Count} row(s) to blob for {Batch}/{File} (ok={Ok})",
            rows.Count,
            batchId,
            fileName,
            ok);
    }
}
