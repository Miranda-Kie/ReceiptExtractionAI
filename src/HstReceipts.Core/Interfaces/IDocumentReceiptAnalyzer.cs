using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

/// <summary>
/// Cloud document analysis (Azure Document Intelligence) that returns structured receipt rows
/// plus source text for AI learning / rule fallback.
/// </summary>
public interface IDocumentReceiptAnalyzer
{
    bool IsAvailable { get; }

    bool CanHandle(string fileName);

    Task<IReadOnlyList<ExtractedReceipt>> AnalyzeAsync(
        Stream stream,
        string receiptLabel,
        CancellationToken cancellationToken = default);
}
