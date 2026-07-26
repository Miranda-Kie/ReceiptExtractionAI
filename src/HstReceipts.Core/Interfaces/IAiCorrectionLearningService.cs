using HstReceipts.Core.Models;



namespace HstReceipts.Core.Interfaces;



/// <summary>

/// Reapplies learned store/currency/money/date profiles on later uploads.

/// </summary>

public interface IAiCorrectionLearningService

{

    /// <summary>

    /// Apply learned profiles onto freshly extracted rows.

    /// </summary>

    Task ApplyLearnedProfilesAsync(

        IList<ExtractedReceipt> receipts,

        CancellationToken cancellationToken = default);

}


