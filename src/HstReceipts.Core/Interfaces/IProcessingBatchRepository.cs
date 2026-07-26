using HstReceipts.Core.Entities;
using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IProcessingBatchRepository
{
    Task<ProcessingBatch> CreateAsync(
        Guid batchId,
        string username,
        int totalFiles,
        CancellationToken cancellationToken = default);

    Task AddResultsAsync(
        Guid batchId,
        string sourceFileName,
        IReadOnlyList<ExtractedReceipt> rows,
        bool fileSucceeded,
        CancellationToken cancellationToken = default);

    Task<ReceiptPipelineBatchStatus> GetStatusAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);
}
