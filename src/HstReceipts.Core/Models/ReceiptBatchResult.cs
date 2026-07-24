namespace HstReceipts.Core.Models;

public class ReceiptBatchResult
{
    public Guid BatchId { get; set; }
    public List<ExtractedReceipt> Receipts { get; set; } = [];
}
