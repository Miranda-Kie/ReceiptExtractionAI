using HstReceipts.Core.Entities;
using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IReceiptRepository
{
    Task<IReadOnlyList<Receipt>> SaveBatchAsync(
        Guid batchId,
        IEnumerable<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Receipt>> GetByBatchIdAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find an existing receipt by InvoiceNumber only (required business key).
    /// </summary>
    Task<(Receipt? Receipt, string MatchKind)> FindMatchAsync(
        string invoiceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert/update receipts matched by InvoiceNumber and append field-level corrections.
    /// InvoiceNumber is required. Returns correction row count written.
    /// </summary>
    Task<(int Inserted, int Updated, int Skipped, int Corrections)> UpsertWithCorrectionsAsync(
        Guid batchId,
        IEnumerable<ExtractedReceipt> receipts,
        string username,
        CancellationToken cancellationToken = default);
}
