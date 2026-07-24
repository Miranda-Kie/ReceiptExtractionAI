using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Learning;

internal static class ExtractionSnapshot
{
    /// <summary>
    /// Freeze the current field values as the pre-edit baseline used by correction detection.
    /// </summary>
    public static void CaptureInitial(ExtractedReceipt receipt)
    {
        receipt.InitialStoreName ??= receipt.StoreName;
        receipt.InitialInvoiceNumber ??= receipt.InvoiceNumber;
        receipt.InitialCurrency ??= receipt.Currency;
        receipt.InitialSubtotal ??= receipt.Subtotal;
        receipt.InitialGstHst ??= receipt.GstHst;
        receipt.InitialTotalAmount ??= receipt.TotalAmount;
        receipt.InitialReceiptDate ??= receipt.ReceiptDate;
        receipt.InitialTransactionTime ??= receipt.TransactionTime;
    }
}
