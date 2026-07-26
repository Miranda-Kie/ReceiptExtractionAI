using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IReceiptProcessingService
{
    Task<ReceiptBatchResult> ProcessUploadsAsync(
        IEnumerable<UploadedReceiptFile> files,
        bool allowAzureServices = false,
        CancellationToken cancellationToken = default);
}
