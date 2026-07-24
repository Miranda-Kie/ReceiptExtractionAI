namespace HstReceipts.Core.Models;

/// <summary>Which receipt columns to include when exporting Excel.</summary>
public class ExcelExportColumns
{
    public bool StoreName { get; set; } = true;
    public bool InvoiceNumber { get; set; } = true;
    public bool Currency { get; set; } = true;
    public bool TransactionTime { get; set; } = true;
    public bool Subtotal { get; set; } = true;
    public bool GstHst { get; set; } = true;
    public bool TotalAmount { get; set; } = true;
    public bool ReceiptDate { get; set; } = true;

    public static ExcelExportColumns All() => new();

    public static ExcelExportColumns FromSelected(IEnumerable<string>? selected)
    {
        var set = new HashSet<string>(
            selected ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        // If nothing posted, keep the default "all selected" behavior.
        if (set.Count == 0)
        {
            return All();
        }

        return new ExcelExportColumns
        {
            StoreName = set.Contains(nameof(StoreName)),
            InvoiceNumber = set.Contains(nameof(InvoiceNumber)),
            Currency = set.Contains(nameof(Currency)),
            TransactionTime = set.Contains(nameof(TransactionTime)),
            Subtotal = set.Contains(nameof(Subtotal)),
            GstHst = set.Contains(nameof(GstHst)) || set.Contains("HstGst") || set.Contains("GstHst"),
            TotalAmount = set.Contains(nameof(TotalAmount)),
            ReceiptDate = set.Contains(nameof(ReceiptDate)) || set.Contains("Date")
        };
    }

    public bool HasAnyColumn =>
        StoreName || InvoiceNumber || Currency ||
        TransactionTime || Subtotal || GstHst || TotalAmount || ReceiptDate;
}
