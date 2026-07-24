namespace HstReceipts.Web.Models;

/// <summary>Editable preview fields posted before save / Excel export.</summary>
public class ReceiptFieldEdit
{
    public string? StoreName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Currency { get; set; }
    public string? TransactionTime { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? GstHst { get; set; }
    public decimal? TotalAmount { get; set; }
    public DateOnly? ReceiptDate { get; set; }
}
