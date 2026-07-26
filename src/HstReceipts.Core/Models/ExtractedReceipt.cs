namespace HstReceipts.Core.Models;

/// <summary>
/// One extracted receipt row.
/// Strings: ReceiptName, StoreName, InvoiceNumber, Currency, TransactionTime.
/// Numbers: Subtotal, HST/GST, TotalAmount. Date: ReceiptDate.
/// </summary>
public class ExtractedReceipt
{
    /// <summary>String — source file path / receipt label (internal; not shown in preview/DB).</summary>
    public string ReceiptName { get; set; } = string.Empty;

    /// <summary>String — merchant / store name.</summary>
    public string? StoreName { get; set; }

    /// <summary>String — receipt / invoice number.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>String — currency code (CAD, USD, …).</summary>
    public string? Currency { get; set; }

    /// <summary>String — transaction clock time when available (HH:mm:ss).</summary>
    public string? TransactionTime { get; set; }

    /// <summary>Number — amount before tax.</summary>
    public decimal? Subtotal { get; set; }

    /// <summary>Number — Tax amount.</summary>
    public decimal? GstHst { get; set; }

    /// <summary>Number — receipt total.</summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>Date — receipt / invoice date.</summary>
    public DateOnly? ReceiptDate { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public string? InitialStoreName { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public string? InitialInvoiceNumber { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public string? InitialCurrency { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public decimal? InitialSubtotal { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public decimal? InitialGstHst { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public decimal? InitialTotalAmount { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public DateOnly? InitialReceiptDate { get; set; }

    /// <summary>OCR snapshot before user edits (for correction learning).</summary>
    public string? InitialTransactionTime { get; set; }

    /// <summary>Truncated OCR / PDF text used for AI learning on Export Excel.</summary>
    public string? SourceTextPreview { get; set; }

    public List<string> Warnings { get; set; } = [];
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
