using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IReceiptFieldExtractor
{
    /// <summary>
    /// Extracts one or more receipts from document text.
    /// A single file may contain multiple receipts.
    /// </summary>
    IReadOnlyList<ExtractedReceipt> ExtractAll(string text, string sourceFileName);
}
