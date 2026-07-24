using System.Globalization;
using System.Text.RegularExpressions;
using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Learning;

/// <summary>
/// Accepts LLM field proposals only when they look plausible and (where required)
/// appear in the OCR text — prevents inventing totals/invoices not on the slip.
/// </summary>
public static class LlmFieldProposalValidator
{
    public static bool NeedsEnrichment(ExtractedReceipt receipt)
    {
        if (receipt is null || string.IsNullOrWhiteSpace(receipt.SourceTextPreview))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(receipt.StoreName)
               || receipt.ReceiptDate is null
               || receipt.TotalAmount is null
               || receipt.GstHst is null
               || receipt.Subtotal is null
               || string.IsNullOrWhiteSpace(receipt.InvoiceNumber)
               || string.IsNullOrWhiteSpace(receipt.Currency)
               || LearnedMoneyDateHints.AmountsFailAuthentication(receipt);
    }

    public static IReadOnlyList<string> ListMissingFields(ExtractedReceipt receipt)
    {
        var missing = new List<string>();
        var amountsBroken = LearnedMoneyDateHints.AmountsFailAuthentication(receipt);

        if (string.IsNullOrWhiteSpace(receipt.StoreName))
        {
            missing.Add("storeName");
        }

        if (string.IsNullOrWhiteSpace(receipt.InvoiceNumber))
        {
            missing.Add("invoiceNumber");
        }

        if (string.IsNullOrWhiteSpace(receipt.Currency))
        {
            missing.Add("currency");
        }

        if (receipt.ReceiptDate is null)
        {
            missing.Add("receiptDate");
        }

        if (string.IsNullOrWhiteSpace(receipt.TransactionTime))
        {
            missing.Add("transactionTime");
        }

        if (receipt.Subtotal is null || amountsBroken)
        {
            missing.Add("subtotal");
        }

        if (receipt.GstHst is null || amountsBroken)
        {
            missing.Add("gstHst");
        }

        if (receipt.TotalAmount is null || amountsBroken)
        {
            missing.Add("totalAmount");
        }

        return missing;
    }

    /// <summary>
    /// Applies accepted proposals onto <paramref name="receipt"/>. Returns count of fields filled.
    /// </summary>
    public static int ApplyValidated(
        ExtractedReceipt receipt,
        LlmFieldProposal proposal,
        string ocrText)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(proposal);

        var filled = 0;
        var source = ocrText ?? string.Empty;
        var amountsBroken = LearnedMoneyDateHints.AmountsFailAuthentication(receipt);

        if (string.IsNullOrWhiteSpace(receipt.StoreName) &&
            TryAcceptStoreName(proposal.StoreName, source, out var store))
        {
            receipt.StoreName = store;
            filled++;
        }

        if (string.IsNullOrWhiteSpace(receipt.InvoiceNumber) &&
            TryAcceptInvoice(proposal.InvoiceNumber, source, out var invoice))
        {
            receipt.InvoiceNumber = invoice;
            filled++;
        }

        if (string.IsNullOrWhiteSpace(receipt.Currency) &&
            TryAcceptCurrency(proposal.Currency, out var currency))
        {
            receipt.Currency = currency;
            filled++;
        }

        if (receipt.ReceiptDate is null &&
            TryAcceptDate(proposal.ReceiptDate, out var date))
        {
            receipt.ReceiptDate = date;
            filled++;
        }

        if (string.IsNullOrWhiteSpace(receipt.TransactionTime) &&
            TryAcceptTime(proposal.TransactionTime, out var time))
        {
            receipt.TransactionTime = time;
            filled++;
        }

        if ((receipt.Subtotal is null || amountsBroken) &&
            TryAcceptMoney(proposal.Subtotal, source, requireInOcr: true, out var subtotal) &&
            (receipt.Subtotal is null || Math.Abs(receipt.Subtotal.Value - subtotal) > 0.005m))
        {
            receipt.Subtotal = subtotal;
            filled++;
        }

        if ((receipt.GstHst is null || amountsBroken) &&
            TryAcceptMoney(proposal.GstHst, source, requireInOcr: true, out var tax) &&
            (receipt.GstHst is null || Math.Abs(receipt.GstHst.Value - tax) > 0.005m))
        {
            receipt.GstHst = tax;
            filled++;
        }

        if ((receipt.TotalAmount is null || amountsBroken) &&
            TryAcceptMoney(proposal.TotalAmount, source, requireInOcr: true, out var total) &&
            (receipt.TotalAmount is null || Math.Abs(receipt.TotalAmount.Value - total) > 0.005m))
        {
            receipt.TotalAmount = total;
            filled++;
        }

        if (filled > 0)
        {
            var evidence = string.IsNullOrWhiteSpace(proposal.Evidence)
                ? "OCR labels"
                : proposal.Evidence.Trim();
            receipt.Warnings.Add(
                amountsBroken
                    ? $"AI corrected {filled} amount field(s) from OCR ({evidence})."
                    : $"AI filled {filled} missing field(s) from OCR ({evidence}).");
        }

        return filled;
    }

    public static bool TryAcceptStoreName(string? proposed, string ocr, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        var trimmed = proposed.Trim();
        if (trimmed.Length is < 3 or > 80)
        {
            return false;
        }

        // Prefer names that appear (loosely) in OCR; allow short brand names with digit/letter mix.
        if (!string.IsNullOrWhiteSpace(ocr))
        {
            var compactOcr = Compact(ocr);
            var compactName = Compact(trimmed);
            if (compactName.Length >= 4 &&
                !compactOcr.Contains(compactName, StringComparison.OrdinalIgnoreCase))
            {
                // Allow if most significant tokens appear.
                var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(t => t.Length >= 4)
                    .ToList();
                if (tokens.Count == 0 ||
                    tokens.Count(t => ocr.Contains(t, StringComparison.OrdinalIgnoreCase)) < Math.Max(1, tokens.Count / 2))
                {
                    return false;
                }
            }
        }

        value = trimmed;
        return true;
    }

    public static bool TryAcceptInvoice(string? proposed, string ocr, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        var trimmed = proposed.Trim().TrimEnd('.', ',', ';');
        if (trimmed.Length is < 4 or > 32)
        {
            return false;
        }

        if (!trimmed.Any(char.IsDigit))
        {
            return false;
        }

        // Must appear in OCR (prevents inventing receipt ids).
        if (string.IsNullOrWhiteSpace(ocr) || !ContainsToken(ocr, trimmed))
        {
            return false;
        }

        value = trimmed;
        return true;
    }

    public static bool TryAcceptCurrency(string? proposed, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        var code = proposed.Trim().ToUpperInvariant();
        if (code is not ("CAD" or "USD" or "EUR" or "GBP" or "MXN" or "CNY" or "JPY"))
        {
            return false;
        }

        value = code;
        return true;
    }

    public static bool TryAcceptDate(string? proposed, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        if (!DateOnly.TryParse(proposed.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date) &&
            !DateOnly.TryParseExact(
                proposed.Trim(),
                ["yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return false;
        }

        var year = date.Year;
        return year is >= 2018 and <= 2035;
    }

    public static bool TryAcceptTime(string? proposed, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        var trimmed = proposed.Trim();
        if (!TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
            !TimeOnly.TryParseExact(
                trimmed,
                ["HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
        {
            return false;
        }

        value = time.Second == 0 && !trimmed.Contains(':')
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : time.ToString(trimmed.Count(c => c == ':') >= 2 ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryAcceptMoney(string? proposed, string ocr, bool requireInOcr, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(proposed))
        {
            return false;
        }

        var raw = proposed.Trim().Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount is < 0m or > 1_000_000m)
        {
            return false;
        }

        if (requireInOcr && !string.IsNullOrWhiteSpace(ocr))
        {
            var token = amount.ToString("0.00", CultureInfo.InvariantCulture);
            var alt = amount.ToString("0.##", CultureInfo.InvariantCulture);
            var comma = token.Replace('.', ',');
            if (!ocr.Contains(token, StringComparison.Ordinal) &&
                !ocr.Contains(alt, StringComparison.Ordinal) &&
                !ocr.Contains(comma, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsToken(string source, string token)
    {
        var escaped = Regex.Escape(token.Trim());
        return Regex.IsMatch(source, $@"(?<![\w-]){escaped}(?![\w-])", RegexOptions.IgnoreCase);
    }

    private static string Compact(string value)
        => Regex.Replace(value, @"[^A-Za-z0-9]", string.Empty);
}
