using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

/// <summary>
/// Uses an LLM to learn extraction hints from corrected fields + receipt text on Export Excel,
/// and reapplies learned store/currency/money/date profiles on later uploads.
/// </summary>
public interface IAiCorrectionLearningService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Analyze corrected receipts (with OCR text when available) and upsert AI profiles in the DB.
    /// </summary>
    Task<AiLearningResult> LearnFromCorrectedReceiptsAsync(
        IReadOnlyList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply learned profiles onto freshly extracted rows.
    /// </summary>
    Task ApplyLearnedProfilesAsync(
        IList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default);
}
