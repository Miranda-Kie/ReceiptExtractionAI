namespace HstReceipts.Core.Entities;

/// <summary>
/// AI-generated extraction profile for a receipt family (from Export Excel corrections).
/// </summary>
public class ReceiptAiProfile
{
    public Guid Id { get; set; }

    public string SimilarityKey { get; set; } = string.Empty;

    public string? CanonicalStoreName { get; set; }
    public string? Currency { get; set; }

    /// <summary>JSON array of OCR store-name aliases.</summary>
    public string? StoreNameAliasesJson { get; set; }

    /// <summary>Optional invoice/order id pattern hint from the model.</summary>
    public string? InvoiceNumberHint { get; set; }

    /// <summary>How to find the receipt date on similar slips (e.g. label:Invoice Date).</summary>
    public string? ReceiptDateHint { get; set; }

    /// <summary>How to find subtotal (e.g. label:Sub Total).</summary>
    public string? SubtotalHint { get; set; }

    /// <summary>How to find HST/GST (e.g. label:HST).</summary>
    public string? GstHstHint { get; set; }

    /// <summary>How to find total (e.g. label:Total after tax).</summary>
    public string? TotalAmountHint { get; set; }

    /// <summary>Short model notes for debugging.</summary>
    public string? Notes { get; set; }

    /// <summary>Raw model JSON response (truncated).</summary>
    public string? RawResponse { get; set; }

    /// <summary>Last updated time in Eastern Time (server-local EST/EDT).</summary>
    public DateTime ModifiedAtEst { get; set; }
}
