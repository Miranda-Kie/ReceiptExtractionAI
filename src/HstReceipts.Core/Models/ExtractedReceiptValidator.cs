namespace HstReceipts.Core.Models;

/// <summary>
/// Validates extracted receipt fields against the expected column types.
/// </summary>
public static class ExtractedReceiptValidator
{
    public static void Apply(ExtractedReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (!string.IsNullOrWhiteSpace(receipt.ReceiptName))
        {
            receipt.ReceiptName = receipt.ReceiptName.Trim();
        }

        receipt.InvoiceNumber = NormalizeOptionalString(receipt.InvoiceNumber, receipt, "InvoiceNumber");
        if (string.IsNullOrWhiteSpace(receipt.InvoiceNumber))
        {
            const string message = "InvoiceNumber is required and cannot be empty.";
            receipt.Success = false;
            receipt.ErrorMessage ??= message;
            AddUniqueWarning(receipt, message);
        }

        receipt.StoreName = NormalizeOptionalString(receipt.StoreName, receipt, "StoreName");
        if (string.IsNullOrWhiteSpace(receipt.StoreName))
        {
            const string message = "StoreName is required and cannot be empty.";
            receipt.Success = false;
            receipt.ErrorMessage ??= message;
            AddUniqueWarning(receipt, message);
        }

        receipt.Currency = NormalizeCurrency(receipt.Currency, receipt);
        receipt.TransactionTime = NormalizeTransactionTime(receipt.TransactionTime, receipt);

        if (receipt.Subtotal is not null)
        {
            if (!IsValidMoneyAmount(receipt.Subtotal.Value))
            {
                AddUniqueWarning(receipt, "Subtotal must be a valid number.");
                receipt.Subtotal = null;
            }
            else
            {
                receipt.Subtotal = decimal.Round(receipt.Subtotal.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        if (receipt.GstHst is not null)
        {
            if (!IsValidMoneyAmount(receipt.GstHst.Value))
            {
                AddUniqueWarning(receipt, "HST/GST must be a valid number.");
                receipt.GstHst = null;
            }
            else
            {
                receipt.GstHst = decimal.Round(receipt.GstHst.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        if (receipt.TotalAmount is not null)
        {
            if (!IsValidMoneyAmount(receipt.TotalAmount.Value))
            {
                AddUniqueWarning(receipt, "TotalAmount must be a valid number.");
                receipt.TotalAmount = null;
            }
            else
            {
                receipt.TotalAmount = decimal.Round(receipt.TotalAmount.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        if (receipt.ReceiptDate is not null)
        {
            if (!IsValidReceiptDate(receipt.ReceiptDate.Value))
            {
                AddUniqueWarning(receipt, "Date must be a valid date (yyyy-MM-dd).");
                receipt.ReceiptDate = null;
            }
        }

        if (receipt.ReceiptDate is null)
        {
            const string message = "Date is required and cannot be empty.";
            receipt.Success = false;
            receipt.ErrorMessage ??= message;
            AddUniqueWarning(receipt, message);
        }

        AuthenticateAmounts(receipt);
    }

    /// <summary>
    /// Requires TotalAmount = Subtotal + HST/GST (within 2 cents) when all three values are present.
    /// </summary>
    public static void AuthenticateAmounts(ExtractedReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (receipt.Subtotal is null || receipt.GstHst is null || receipt.TotalAmount is null)
        {
            AddUniqueWarning(
                receipt,
                "Amount authentication skipped: Subtotal, HST/GST, and TotalAmount are all required.");
            return;
        }

        var expected = decimal.Round(
            receipt.Subtotal.Value + receipt.GstHst.Value,
            2,
            MidpointRounding.AwayFromZero);
        var actual = decimal.Round(receipt.TotalAmount.Value, 2, MidpointRounding.AwayFromZero);
        var delta = Math.Abs(actual - expected);

        if (delta <= 0.02m)
        {
            return;
        }

        var message =
            $"Amount authentication failed: TotalAmount ({actual:0.00}) must equal Subtotal ({receipt.Subtotal.Value:0.00}) + HST/GST ({receipt.GstHst.Value:0.00}) = {expected:0.00} (difference {delta:0.00}).";

        receipt.Success = false;
        receipt.ErrorMessage = message;
        AddUniqueWarning(receipt, message);
    }

    private static string? NormalizeOptionalString(string? value, ExtractedReceipt receipt, string fieldName)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            AddUniqueWarning(receipt, $"{fieldName} must be a non-empty string when present.");
            return null;
        }

        return trimmed;
    }

    private static string? NormalizeCurrency(string? value, ExtractedReceipt receipt)
    {
        var normalized = NormalizeOptionalString(value, receipt, "Currency");
        if (normalized is null)
        {
            return null;
        }

        normalized = normalized.ToUpperInvariant();
        if (normalized is not ("CAD" or "USD" or "EUR" or "GBP" or "AUD" or "MXN" or "CNY" or "JPY"))
        {
            // Still keep unknown codes; warn only for nonsense length.
            if (normalized.Length is < 3 or > 4)
            {
                AddUniqueWarning(receipt, "Currency must be a short currency code string (e.g. CAD, USD).");
                return null;
            }
        }

        return normalized;
    }

    private static string? NormalizeTransactionTime(string? value, ExtractedReceipt receipt)
    {
        var normalized = NormalizeOptionalString(value, receipt, "TransactionTime");
        if (normalized is null)
        {
            return null;
        }

        // Prefer an explicit time token (strip leading date if present).
        var timeMatch = System.Text.RegularExpressions.Regex.Match(
            normalized,
            @"\b([01]?\d|2[0-3]):([0-5]\d)(?::([0-5]\d))?\b");
        if (timeMatch.Success &&
            int.TryParse(timeMatch.Groups[1].Value, out var hour) &&
            int.TryParse(timeMatch.Groups[2].Value, out var minute))
        {
            var second = timeMatch.Groups[3].Success && int.TryParse(timeMatch.Groups[3].Value, out var s)
                ? s
                : 0;
            return new TimeOnly(hour, minute, second).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Date-only values are not valid for TransactionTime anymore.
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d{4}-\d{2}-\d{2}$"))
        {
            AddUniqueWarning(receipt, "TransactionTime must be a time string (HH:mm:ss), not a date.");
            return null;
        }

        AddUniqueWarning(receipt, "TransactionTime must be a time string (HH:mm:ss).");
        return null;
    }

    private static bool IsValidMoneyAmount(decimal value)
        => value is >= -1_000_000m and <= 1_000_000m;

    private static bool IsValidReceiptDate(DateOnly date)
        => date.Year is >= 2000 and <= 2100;

    private static void AddUniqueWarning(ExtractedReceipt receipt, string warning)
    {
        if (!receipt.Warnings.Contains(warning, StringComparer.Ordinal))
        {
            receipt.Warnings.Add(warning);
        }
    }
}
