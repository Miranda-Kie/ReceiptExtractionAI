using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Learning;

/// <summary>
/// Detects which preview fields the user changed vs the OCR snapshot.
/// Used to persist / send only corrections (saves DB noise and AI tokens).
/// </summary>
internal static class FieldChangeDetector
{
    public static bool HasAnyCorrection(ExtractedReceipt receipt)
        => StoreNameChanged(receipt)
           || InvoiceNumberChanged(receipt)
           || CurrencyChanged(receipt)
           || TransactionTimeChanged(receipt)
           || SubtotalChanged(receipt)
           || GstHstChanged(receipt)
           || TotalAmountChanged(receipt)
           || ReceiptDateChanged(receipt);

    public static bool StoreNameChanged(ExtractedReceipt receipt)
        => TextChanged(receipt.InitialStoreName, receipt.StoreName);

    public static bool InvoiceNumberChanged(ExtractedReceipt receipt)
        => TextChanged(receipt.InitialInvoiceNumber, receipt.InvoiceNumber);

    public static bool CurrencyChanged(ExtractedReceipt receipt)
        => TextChanged(receipt.InitialCurrency, receipt.Currency);

    public static bool TransactionTimeChanged(ExtractedReceipt receipt)
        => TextChanged(receipt.InitialTransactionTime, receipt.TransactionTime);

    public static bool SubtotalChanged(ExtractedReceipt receipt)
        => MoneyChanged(receipt.InitialSubtotal, receipt.Subtotal);

    public static bool GstHstChanged(ExtractedReceipt receipt)
        => MoneyChanged(receipt.InitialGstHst, receipt.GstHst);

    public static bool TotalAmountChanged(ExtractedReceipt receipt)
        => MoneyChanged(receipt.InitialTotalAmount, receipt.TotalAmount);

    public static bool ReceiptDateChanged(ExtractedReceipt receipt)
        => receipt.InitialReceiptDate != receipt.ReceiptDate
           && (receipt.InitialReceiptDate is not null || receipt.ReceiptDate is not null);

    public static List<string> DescribeChanges(ExtractedReceipt receipt)
    {
        var lines = new List<string>();

        if (StoreNameChanged(receipt))
        {
            lines.Add($"StoreName: {FormatText(receipt.InitialStoreName)} → {FormatText(receipt.StoreName)}");
        }

        if (InvoiceNumberChanged(receipt))
        {
            lines.Add($"InvoiceNumber: {FormatText(receipt.InitialInvoiceNumber)} → {FormatText(receipt.InvoiceNumber)}");
        }

        if (CurrencyChanged(receipt))
        {
            lines.Add($"Currency: {FormatText(receipt.InitialCurrency)} → {FormatText(receipt.Currency)}");
        }

        if (TransactionTimeChanged(receipt))
        {
            lines.Add($"TransactionTime: {FormatText(receipt.InitialTransactionTime)} → {FormatText(receipt.TransactionTime)}");
        }

        if (SubtotalChanged(receipt))
        {
            lines.Add($"Subtotal: {FormatMoney(receipt.InitialSubtotal)} → {FormatMoney(receipt.Subtotal)}");
        }

        if (GstHstChanged(receipt))
        {
            lines.Add($"HST/GST: {FormatMoney(receipt.InitialGstHst)} → {FormatMoney(receipt.GstHst)}");
        }

        if (TotalAmountChanged(receipt))
        {
            lines.Add($"TotalAmount: {FormatMoney(receipt.InitialTotalAmount)} → {FormatMoney(receipt.TotalAmount)}");
        }

        if (ReceiptDateChanged(receipt))
        {
            lines.Add($"Date: {FormatDate(receipt.InitialReceiptDate)} → {FormatDate(receipt.ReceiptDate)}");
        }

        return lines;
    }

    private static bool TextChanged(string? before, string? after)
    {
        var left = NormalizeText(before);
        var right = NormalizeText(after);
        return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MoneyChanged(decimal? before, decimal? after)
    {
        if (before is null && after is null)
        {
            return false;
        }

        if (before is null || after is null)
        {
            return true;
        }

        return Math.Abs(before.Value - after.Value) > 0.005m;
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();

    private static string FormatMoney(decimal? value)
        => value is null ? "(empty)" : value.Value.ToString("0.00");

    private static string FormatDate(DateOnly? value)
        => value is null ? "(empty)" : value.Value.ToString("yyyy-MM-dd");
}
