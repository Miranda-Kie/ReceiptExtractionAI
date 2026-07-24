using System.Globalization;
using System.Text.RegularExpressions;
using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Learning;

/// <summary>
/// Builds and applies label-based hints for date / money fields from user corrections.
/// Hints look like <c>label:HST</c> or <c>label:Total after tax</c> (never stamp a prior bill's amount).
/// </summary>
public static class LearnedMoneyDateHints
{
    public static string? BuildMoneyHint(ExtractedReceipt receipt, decimal correctedAmount)
    {
        var source = receipt.SourceTextPreview ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var label = DetectMoneyLabelNearAmount(source, correctedAmount);
        return string.IsNullOrWhiteSpace(label) ? null : $"label:{label}";
    }

    public static string? BuildDateHint(ExtractedReceipt receipt, DateOnly correctedDate)
    {
        var source = receipt.SourceTextPreview ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var label = DetectDateLabelNearDate(source, correctedDate);
        return string.IsNullOrWhiteSpace(label) ? null : $"label:{label}";
    }

    public static bool TryApplyMoneyHint(
        string? hint,
        ExtractedReceipt receipt,
        Action<decimal> assign,
        Func<decimal?> currentGetter,
        string fieldDisplayName,
        bool forceReplace)
    {
        if (string.IsNullOrWhiteSpace(hint) ||
            string.IsNullOrWhiteSpace(receipt.SourceTextPreview))
        {
            return false;
        }

        if (!hint.Trim().StartsWith("label:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var label = hint.Trim()["label:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var amount = ExtractMoneyNearLabel(receipt.SourceTextPreview!, label);
        if (amount is null)
        {
            return false;
        }

        var current = currentGetter();
        if (current is not null &&
            Math.Abs(current.Value - amount.Value) <= 0.005m)
        {
            return false;
        }

        if (current is not null && !forceReplace)
        {
            return false;
        }

        var previous = current;
        assign(amount.Value);
        receipt.Warnings.Add(
            $"Applied learned {fieldDisplayName} {amount.Value:0.00} near '{label}'" +
            (previous is null ? "." : $" (was {previous.Value:0.00})."));
        return true;
    }

    public static bool TryApplyDateHint(string? hint, ExtractedReceipt receipt, bool forceReplace)
    {
        if (string.IsNullOrWhiteSpace(hint) ||
            string.IsNullOrWhiteSpace(receipt.SourceTextPreview))
        {
            return false;
        }

        if (!hint.Trim().StartsWith("label:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var label = hint.Trim()["label:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var date = ExtractDateNearLabel(receipt.SourceTextPreview!, label);
        if (date is null)
        {
            return false;
        }

        if (receipt.ReceiptDate == date)
        {
            return false;
        }

        if (receipt.ReceiptDate is not null && !forceReplace)
        {
            return false;
        }

        var previous = receipt.ReceiptDate;
        receipt.ReceiptDate = date;
        receipt.Warnings.Add(
            $"Applied learned date {date:yyyy-MM-dd} near '{label}'" +
            (previous is null ? "." : $" (was {previous:yyyy-MM-dd})."));
        return true;
    }

    public static bool AmountsFailAuthentication(ExtractedReceipt receipt)
    {
        if (receipt.Subtotal is null || receipt.GstHst is null || receipt.TotalAmount is null)
        {
            return false;
        }

        var expected = decimal.Round(
            receipt.Subtotal.Value + receipt.GstHst.Value,
            2,
            MidpointRounding.AwayFromZero);
        var actual = decimal.Round(receipt.TotalAmount.Value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(actual - expected) > 0.02m;
    }

    public static string? DetectMoneyLabelNearAmount(string source, decimal amount)
    {
        var tokens = MoneyTokens(amount);
        var labels = new (string Label, string Pattern)[]
        {
            ("Total after tax", @"Total\s+after\s+ta[sx]"),
            ("Total Amount Due", @"Total\s+Amount\s+Due"),
            ("Amount Due", @"Amount\s+Due"),
            ("Grand Total", @"Grand\s+Total"),
            ("Credit Card", @"Credit\s+Card"),
            ("Sub Total", @"Sub\s*Total"),
            ("Subtotal", @"Subtotal"),
            ("HST", @"\bHST\b"),
            ("GST", @"\bGST\b"),
            ("TVH", @"\bTVH\b"),
            ("TPS", @"\bTPS\b"),
            ("Tax", @"\bTax\b"),
            ("Total", @"\bTotal\b(?!\s+after)"),
        };

        foreach (var (label, labelPat) in labels)
        {
            foreach (var token in tokens)
            {
                var escaped = Regex.Escape(token);
                // Same line or within ~80 chars after label.
                var pattern =
                    $@"(?im){labelPat}\s*[:#]?\s*.{{0,40}}?{escaped}\b|" +
                    $@"(?im){labelPat}\s*[:#]?\s*(?:\r?\n[^\r\n]{{0,60}}){{0,3}}?.*?\b{escaped}\b";
                if (Regex.IsMatch(source, pattern))
                {
                    return label;
                }
            }
        }

        return null;
    }

    public static string? DetectDateLabelNearDate(string source, DateOnly date)
    {
        var tokens = DateTokens(date);
        var labels = new (string Label, string Pattern)[]
        {
            ("Invoice Date", @"Invoice\s*Date"),
            ("Statement Date", @"Statement\s*Date"),
            ("Bill Date", @"Bill\s*Date"),
            ("Receipt Date", @"Receipt\s*Date"),
            ("Transaction Date", @"Transaction\s*Date"),
            ("DATE", @"\bDATE\b"),
            ("Date", @"\bDate\b"),
        };

        foreach (var (label, labelPat) in labels)
        {
            foreach (var token in tokens)
            {
                var escaped = Regex.Escape(token);
                var pattern =
                    $@"(?im){labelPat}\s*[:#/]?\s*.{{0,30}}?{escaped}|" +
                    $@"(?im){labelPat}\s*[:#/]?\s*(?:\r?\n[^\r\n]{{0,40}}){{0,2}}.*?{escaped}";
                if (Regex.IsMatch(source, pattern))
                {
                    return label;
                }
            }
        }

        return null;
    }

    public static decimal? ExtractMoneyNearLabel(string source, string label)
    {
        var labelPattern = Regex.Escape(label).Replace(@"\ ", @"\s+", StringComparison.Ordinal);
        var lines = source.Split(['\r', '\n'], StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], $@"(?i)\b{labelPattern}\b"))
            {
                continue;
            }

            // Skip HST registration lines.
            if (Regex.IsMatch(lines[i], @"(?i)\bHST\s*#|\bGST\s*#|RT\d{4}"))
            {
                continue;
            }

            for (var j = i; j <= Math.Min(i + 4, lines.Length - 1); j++)
            {
                if (j > i &&
                    Regex.IsMatch(lines[j], @"(?i)^\s*(Sub\s*Total|Total\s+after|Credit\s*Card|HST|GST)\b") &&
                    !Regex.IsMatch(lines[j], $@"(?i)\b{labelPattern}\b"))
                {
                    // Hit another money label — stop unless it's the same block's amount line.
                    if (j > i + 1)
                    {
                        break;
                    }
                }

                var amount = FirstMoneyOnLine(lines[j]);
                if (amount is not null)
                {
                    return amount;
                }
            }
        }

        // Fallback: label ... amount within 60 chars on one line in full text.
        var m = Regex.Match(
            source,
            $@"(?im)\b{labelPattern}\b\s*[:#]?\s*.{{0,40}}?(\d{{1,6}}[.,]\d{{2}})\b");
        return m.Success ? ParseMoney(m.Groups[1].Value) : null;
    }

    public static DateOnly? ExtractDateNearLabel(string source, string label)
    {
        var labelPattern = Regex.Escape(label).Replace(@"\ ", @"\s+", StringComparison.Ordinal);
        var lines = source.Split(['\r', '\n'], StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], $@"(?i)\b{labelPattern}\b"))
            {
                continue;
            }

            for (var j = i; j <= Math.Min(i + 3, lines.Length - 1); j++)
            {
                var date = FirstDateOnLine(lines[j]);
                if (date is not null)
                {
                    return date;
                }
            }
        }

        var m = Regex.Match(
            source,
            $@"(?im)\b{labelPattern}\b\s*[:#/]?\s*.{{0,40}}?(20\d{{2}}[/-]\d{{1,2}}[/-]\d{{1,2}}|\d{{1,2}}[/-]\d{{1,2}}[/-](?:20)?\d{{2}})");
        return m.Success ? ParseDate(m.Groups[1].Value) : null;
    }

    private static IReadOnlyList<string> MoneyTokens(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var dot = rounded.ToString("0.00", CultureInfo.InvariantCulture);
        var comma = dot.Replace('.', ',');
        return [dot, comma];
    }

    private static IReadOnlyList<string> DateTokens(DateOnly date)
    {
        var ymd = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ymdSlash = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var mdY = date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        var mdYShort = date.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        var md = date.ToString("MM/dd", CultureInfo.InvariantCulture);
        return [ymd, ymdSlash, mdY, mdYShort, md, date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)];
    }

    private static decimal? FirstMoneyOnLine(string line)
    {
        foreach (Match m in Regex.Matches(line, @"\b(\d{1,6}[.,]\d{2})\b"))
        {
            var parsed = ParseMoney(m.Groups[1].Value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal? ParseMoney(string raw)
    {
        var normalized = raw.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        return amount is >= 0 and < 1_000_000m ? amount : null;
    }

    private static DateOnly? FirstDateOnLine(string line)
    {
        var patterns = new[]
        {
            @"\b(20\d{2})[/-](\d{1,2})[/-](\d{1,2})\b",
            @"\b(\d{1,2})[/-](\d{1,2})[/-](20\d{2})\b",
            @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{2})\b"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(line, pattern);
            if (!m.Success)
            {
                continue;
            }

            var parsed = ParseDate(m.Value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateOnly? ParseDate(string raw)
    {
        if (DateOnly.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d.Year is >= 2018 and <= 2035 ? d : null;
        }

        if (DateOnly.TryParseExact(
                raw.Trim(),
                ["yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy", "MM/dd/yy", "M/d/yy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out d))
        {
            if (d.Year < 100)
            {
                d = new DateOnly(2000 + d.Year, d.Month, d.Day);
            }

            return d.Year is >= 2018 and <= 2035 ? d : null;
        }

        return null;
    }
}
