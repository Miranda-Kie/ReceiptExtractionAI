using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

/// <summary>
/// Uses an LLM to propose missing/weak receipt fields from OCR text, then validates
/// proposals locally before applying (hallucination guard).
/// </summary>
public interface IAiFieldEnrichmentService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Fills missing fields on receipts that still need them after rule extraction / profiles.
    /// </summary>
    Task EnrichMissingFieldsAsync(
        IList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default);
}
