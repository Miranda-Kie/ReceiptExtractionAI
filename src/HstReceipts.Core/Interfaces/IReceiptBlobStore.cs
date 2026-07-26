using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IReceiptBlobStore
{
    bool IsAvailable { get; }

    Task UploadInboxAsync(
        Guid batchId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default);

    Task WriteManifestAsync(
        Guid batchId,
        int totalFiles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores preview extraction for a file in blob (not the Receipts SQL table).
    /// Updates the batch manifest counters/status.
    /// </summary>
    Task WriteFileResultAsync(
        Guid batchId,
        string sourceFileName,
        IReadOnlyList<ExtractedReceipt> rows,
        bool fileSucceeded,
        CancellationToken cancellationToken = default);

    Task<ReceiptPipelineBatchStatus> GetBatchStatusAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);
}

public sealed class ReceiptPipelineBatchStatus
{
    public Guid BatchId { get; set; }
    public string Status { get; set; } = "processing"; // processing | completed | failed
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<ExtractedReceipt> Receipts { get; set; } = [];
}
