using System.Text.Json;
using System.Text.RegularExpressions;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HstReceipts.Infrastructure.Learning;

/// <summary>
/// Applies previously stored receipt AI profiles during extraction.
/// Admin correction-learning (train-on-export) has been removed.
/// </summary>
public sealed class OpenAiCorrectionLearningService : IAiCorrectionLearningService
{
    private readonly ReceiptDbContext _db;
    private readonly ILogger<OpenAiCorrectionLearningService> _logger;

    public OpenAiCorrectionLearningService(
        ReceiptDbContext db,
        ILogger<OpenAiCorrectionLearningService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ApplyLearnedProfilesAsync(
        IList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default)
    {
        if (receipts.Count == 0)
        {
            return;
        }

        List<ReceiptAiProfile> profiles;
        try
        {
            profiles = await _db.ReceiptAiProfiles.AsNoTracking().ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load AI profiles for apply.");
            return;
        }

        if (profiles.Count == 0)
        {
            return;
        }

        var byKey = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.SimilarityKey))
            .GroupBy(p => p.SimilarityKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var applied = 0;
        foreach (var receipt in receipts)
        {
            var profile = FindProfile(receipt, byKey, profiles);
            if (profile is null)
            {
                continue;
            }

            if (ApplyProfile(profile, receipt))
            {
                applied++;
            }
        }

        if (applied > 0)
        {
            _logger.LogInformation("Applied learned AI profiles to {Count} receipt(s).", applied);
        }
    }
    private static ReceiptAiProfile? FindProfile(
        ExtractedReceipt receipt,
        IReadOnlyDictionary<string, ReceiptAiProfile> byKey,
        IReadOnlyList<ReceiptAiProfile> profiles)
    {
        // Prefer store-name match first. Filename keys can collide (e.g. "AI Premium Mart"
        // vs "AI Premium Food Mart") and would otherwise apply the wrong invoice hint.
        var byStore = FindProfileByStore(receipt.StoreName, profiles);
        if (byStore is not null)
        {
            return byStore;
        }

        var key = ReceiptSimilarityKey.Build(receipt.ReceiptName);
        if (!string.IsNullOrWhiteSpace(key) && byKey.TryGetValue(key, out var byName))
        {
            return byName;
        }

        return null;
    }

    private static ReceiptAiProfile? FindProfileByStore(
        string? storeName,
        IReadOnlyList<ReceiptAiProfile> profiles)
    {
        var store = storeName?.Trim();
        if (string.IsNullOrWhiteSpace(store))
        {
            return null;
        }

        foreach (var profile in profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.CanonicalStoreName) &&
                string.Equals(profile.CanonicalStoreName.Trim(), store, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }

            foreach (var alias in ReadAliases(profile.StoreNameAliasesJson))
            {
                if (string.Equals(alias, store, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
        }

        return null;
    }

    private static bool ApplyProfile(ReceiptAiProfile profile, ExtractedReceipt receipt)
    {
        var changed = false;
        var amountsBroken = LearnedMoneyDateHints.AmountsFailAuthentication(receipt);

        if (!string.IsNullOrWhiteSpace(profile.CanonicalStoreName) &&
            !string.Equals(receipt.StoreName?.Trim(), profile.CanonicalStoreName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var previous = receipt.StoreName;
            receipt.StoreName = profile.CanonicalStoreName.Trim();
            receipt.Warnings.Add(
                $"Applied learned store name '{receipt.StoreName}'" +
                (string.IsNullOrWhiteSpace(previous) ? "." : $" (was '{previous}')."));
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(profile.Currency) &&
            !string.Equals(receipt.Currency?.Trim(), profile.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var previous = receipt.Currency;
            receipt.Currency = profile.Currency.Trim();
            receipt.Warnings.Add(
                $"Applied learned currency '{receipt.Currency}'" +
                (string.IsNullOrWhiteSpace(previous) ? "." : $" (was '{previous}')."));
            changed = true;
        }

        if (TryApplyInvoiceHint(profile.InvoiceNumberHint, receipt))
        {
            changed = true;
        }

        if (LearnedMoneyDateHints.TryApplyDateHint(
                profile.ReceiptDateHint,
                receipt,
                forceReplace: receipt.ReceiptDate is null))
        {
            changed = true;
        }

        if (LearnedMoneyDateHints.TryApplyMoneyHint(
                profile.SubtotalHint,
                receipt,
                v => receipt.Subtotal = v,
                () => receipt.Subtotal,
                "subtotal",
                forceReplace: receipt.Subtotal is null || amountsBroken))
        {
            changed = true;
            amountsBroken = LearnedMoneyDateHints.AmountsFailAuthentication(receipt);
        }

        if (LearnedMoneyDateHints.TryApplyMoneyHint(
                profile.GstHstHint,
                receipt,
                v => receipt.GstHst = v,
                () => receipt.GstHst,
                "HST/GST",
                forceReplace: receipt.GstHst is null || amountsBroken))
        {
            changed = true;
            amountsBroken = LearnedMoneyDateHints.AmountsFailAuthentication(receipt);
        }

        if (LearnedMoneyDateHints.TryApplyMoneyHint(
                profile.TotalAmountHint,
                receipt,
                v => receipt.TotalAmount = v,
                () => receipt.TotalAmount,
                "total",
                forceReplace: receipt.TotalAmount is null || amountsBroken))
        {
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Applies a learned invoice hint without blindly stamping a prior bill's id onto every upload.
    /// Supports: "label:Account Number", optional ";literal" fallback, capture-group regex, or a literal found in OCR.
    /// </summary>
    private static bool TryApplyInvoiceHint(string? hint, ExtractedReceipt receipt)
    {
        if (!IsUsableInvoiceHint(hint))
        {
            return false;
        }

        var source = receipt.SourceTextPreview ?? string.Empty;
        string? resolved = null;
        string? literalFallback = null;

        foreach (var part in hint!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsUsableInvoiceHint(part))
            {
                continue;
            }

            if (part.StartsWith("label:", StringComparison.OrdinalIgnoreCase))
            {
                var label = part["label:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(source))
                {
                    resolved ??= ExtractInvoiceNearLabel(source, label);
                }

                continue;
            }

            // Older profiles stored a one-off POS stamp (P826â€¦). Treat it as "find this shape
            // on the current OCR" instead of requiring the exact prior receipt id.
            if (LooksLikePerTransactionInvoiceId(part) &&
                Regex.IsMatch(part, @"^P\d{13}$", RegexOptions.IgnoreCase) &&
                !string.IsNullOrWhiteSpace(source))
            {
                var pos = Regex.Match(source, @"\b(P\d{13})\b", RegexOptions.IgnoreCase);
                if (pos.Success)
                {
                    resolved ??= pos.Groups[1].Value.Trim();
                }

                continue;
            }

            if (LooksLikeCaptureRegex(part))
            {
                if (!string.IsNullOrWhiteSpace(source))
                {
                    try
                    {
                        var m = Regex.Match(source, part, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if (m.Success)
                        {
                            var captured = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();
                            // Never accept the pattern text itself as an invoice id.
                            if (!LooksLikeCaptureRegex(captured) &&
                                !captured.Contains('\\') &&
                                captured.Any(char.IsDigit))
                            {
                                resolved ??= captured;
                            }
                        }
                    }
                    catch (RegexParseException)
                    {
                        // Invalid pattern â€” ignore; do not stamp it as a literal id.
                    }
                }

                continue;
            }

            if (ContainsInvoiceToken(source, part))
            {
                resolved ??= part;
            }
            else if (!part.Contains('\\') && !part.Contains('('))
            {
                literalFallback ??= part;
            }
        }

        // Last resort for stable account-style ids only â€” never stamp a prior receipt's
        // per-transaction POS id (e.g. P8260319124148) onto a different upload,
        // and never stamp a regex pattern.
        if (resolved is null &&
            !string.IsNullOrWhiteSpace(literalFallback) &&
            IsWeakInvoiceNumber(receipt.InvoiceNumber) &&
            literalFallback.Length >= 6 &&
            !LooksLikePerTransactionInvoiceId(literalFallback) &&
            !LooksLikeCaptureRegex(literalFallback) &&
            !literalFallback.Contains('\\'))
        {
            resolved = literalFallback;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        resolved = resolved.Trim();
        if (string.Equals(receipt.InvoiceNumber?.Trim(), resolved, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previous = receipt.InvoiceNumber;
        receipt.InvoiceNumber = resolved;
        receipt.Warnings.Add(
            $"Applied learned invoice number '{resolved}'" +
            (string.IsNullOrWhiteSpace(previous) ? "." : $" (was '{previous}')."));
        return true;
    }

    private static bool IsWeakInvoiceNumber(string? invoice)
        => string.IsNullOrWhiteSpace(invoice) || invoice.Trim().Length < 6;
    private static bool LooksLikePerTransactionInvoiceId(string value)
        => Regex.IsMatch(value.Trim(), @"^P\d{10,}$|^FB\d{6,}$", RegexOptions.IgnoreCase);

    private static bool IsUsableInvoiceHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        var value = hint.Trim();
        // Discard prompt placeholders / junk model output.
        if (value.Equals("short", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("string", StringComparison.OrdinalIgnoreCase) ||
            value.Length < 4)
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeCaptureRegex(string hint)
        => hint.Contains('(') && hint.Contains(')') &&
           (hint.Contains('\\') || hint.Contains('[') || hint.Contains('?'));

    private static bool ContainsInvoiceToken(string source, string token)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var escaped = Regex.Escape(token.Trim());
        return Regex.IsMatch(source, $@"(?<![\w-]){escaped}(?![\w-])", RegexOptions.IgnoreCase);
    }
    private static string? ExtractInvoiceNearLabel(string source, string label)
    {
        var labelPattern = Regex.Escape(label).Replace(@"\ ", @"\s+", StringComparison.Ordinal);
        var sameLine = Regex.Match(
            source,
            $@"\b{labelPattern}\s*[:#]?\s*([A-Z0-9][A-Z0-9\-/]{{3,}})",
            RegexOptions.IgnoreCase);
        if (sameLine.Success)
        {
            return sameLine.Groups[1].Value.Trim();
        }

        var lines = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], $@"\b{labelPattern}\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            for (var j = i; j <= Math.Min(i + 3, lines.Length - 1); j++)
            {
                var m = Regex.Match(lines[j], @"\b([A-Z0-9][A-Z0-9\-/]{3,})\b", RegexOptions.IgnoreCase);
                if (m.Success && m.Value.Any(char.IsDigit))
                {
                    return m.Value.Trim();
                }
            }
        }

        return null;
    }
    private static IEnumerable<string> ReadAliases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        List<string>? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            yield break;
        }

        if (parsed is null)
        {
            yield break;
        }

        foreach (var item in parsed)
        {
            if (!string.IsNullOrWhiteSpace(item))
            {
                yield return item.Trim();
            }
        }
    }
}

