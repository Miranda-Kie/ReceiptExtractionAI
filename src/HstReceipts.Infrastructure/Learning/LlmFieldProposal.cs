namespace HstReceipts.Infrastructure.Learning;

/// <summary>Structured LLM response for missing-field fill (camelCase JSON).</summary>
public sealed class LlmFieldProposal
{
    public string? StoreName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Currency { get; set; }
    public string? ReceiptDate { get; set; }
    public string? TransactionTime { get; set; }
    public string? Subtotal { get; set; }
    public string? GstHst { get; set; }
    public string? TotalAmount { get; set; }

    /// <summary>Short note of which OCR labels were used (for warnings / debugging).</summary>
    public string? Evidence { get; set; }
}
