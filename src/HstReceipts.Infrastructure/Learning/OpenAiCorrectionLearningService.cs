using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Learning;

public sealed class OpenAiCorrectionLearningService : IAiCorrectionLearningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly ReceiptDbContext _db;
    private readonly AiLearningOptions _options;
    private readonly IOpenAiUsageRecorder _usageRecorder;
    private readonly ILogger<OpenAiCorrectionLearningService> _logger;

    public OpenAiCorrectionLearningService(
        HttpClient http,
        ReceiptDbContext db,
        IOptions<AiLearningOptions> options,
        IOpenAiUsageRecorder usageRecorder,
        ILogger<OpenAiCorrectionLearningService> logger)
    {
        _http = http;
        _db = db;
        _options = options.Value;
        _usageRecorder = usageRecorder;
        _logger = logger;
    }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.Model);

    public async Task<AiLearningResult> LearnFromCorrectedReceiptsAsync(
        IReadOnlyList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default)
    {
        var result = new AiLearningResult();
        var groups = receipts
            .Where(r => !string.IsNullOrWhiteSpace(r.ReceiptName))
            .Where(FieldChangeDetector.HasAnyCorrection)
            .GroupBy(r => ReceiptSimilarityKey.Build(r.ReceiptName), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        if (groups.Count == 0)
        {
            result.Message = "AI learning skipped: no preview fields were changed vs OCR.";
            _logger.LogInformation(result.Message);
            return result;
        }

        result.GroupsProcessed = groups.Count;
        var fieldsLearned = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = group.ToList();

            // Always persist corrected store/currency/money/date hints so the next similar upload can apply them,
            // even if the OpenAI call fails or is not configured.
            try
            {
                fieldsLearned += await UpsertFromCorrectionsAsync(group.Key, rows, cancellationToken);
                result.ProfilesUpdated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist correction profile for {Key}.", group.Key);
            }

            if (!IsEnabled)
            {
                _logger.LogInformation(
                    "Stored correction profile for {Key}; OpenAI enrichment skipped (API not configured).",
                    group.Key);
                continue;
            }

            if (!await _usageRecorder.TryAcquireAsync("correction_learning", group.Key, cancellationToken))
            {
                _logger.LogWarning(
                    "Skipping OpenAI correction learning for {Key}: daily usage limit reached.",
                    group.Key);
                continue;
            }

            try
            {
                var profile = await AskModelForProfileAsync(group.Key, rows, cancellationToken);
                if (profile is null)
                {
                    continue;
                }

                await UpsertProfileAsync(group.Key, profile, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI learning failed for similarity key {Key}", group.Key);
            }
        }

        result.Ran = true;
        result.FieldsLearned = fieldsLearned;
        result.Message =
            $"AI learning saved {result.ProfilesUpdated} profile(s), {fieldsLearned} field hint(s) " +
            $"(store/currency/invoice/date/subtotal/HST/total).";
        _logger.LogInformation(result.Message);
        return result;
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

    private async Task<int> UpsertFromCorrectionsAsync(
        string similarityKey,
        IReadOnlyList<ExtractedReceipt> rows,
        CancellationToken cancellationToken)
    {
        var storeName = rows
            .Select(r => r.StoreName?.Trim())
            .LastOrDefault(s => !string.IsNullOrWhiteSpace(s));

        // Prefer the profile keyed by corrected store name so filename variants
        // ("AI Premium Mart.pdf" vs "AI Food Mart.pdf") share one learning row.
        var storeKey = !string.IsNullOrWhiteSpace(storeName)
            ? ReceiptSimilarityKey.Build(storeName)
            : string.Empty;
        var profileKey = !string.IsNullOrWhiteSpace(storeKey) ? storeKey : similarityKey;

        var existing = !string.IsNullOrWhiteSpace(storeName)
            ? await _db.ReceiptAiProfiles.FirstOrDefaultAsync(
                p => p.CanonicalStoreName == storeName,
                cancellationToken)
            : null;

        existing ??= !string.IsNullOrWhiteSpace(storeKey)
            ? await _db.ReceiptAiProfiles.FirstOrDefaultAsync(
                p => p.SimilarityKey == storeKey,
                cancellationToken)
            : null;

        existing ??= await _db.ReceiptAiProfiles
            .FirstOrDefaultAsync(p => p.SimilarityKey == similarityKey, cancellationToken);

        if (existing is null)
        {
            existing = new ReceiptAiProfile
            {
                Id = Guid.NewGuid(),
                SimilarityKey = profileKey
            };
            _db.ReceiptAiProfiles.Add(existing);
        }
        else if (!string.IsNullOrWhiteSpace(storeKey) &&
                 !string.Equals(existing.SimilarityKey, storeKey, StringComparison.OrdinalIgnoreCase) &&
                 !await _db.ReceiptAiProfiles.AnyAsync(p => p.SimilarityKey == storeKey, cancellationToken))
        {
            existing.SimilarityKey = storeKey;
        }

        var fieldsLearned = 0;

        var storeChanged = rows.LastOrDefault(FieldChangeDetector.StoreNameChanged);
        if (storeChanged is not null && !string.IsNullOrWhiteSpace(storeChanged.StoreName))
        {
            existing.CanonicalStoreName = storeChanged.StoreName.Trim();
            MergeAlias(existing, storeChanged.InitialStoreName);
            fieldsLearned++;
        }

        var currencyChanged = rows.LastOrDefault(FieldChangeDetector.CurrencyChanged);
        if (currencyChanged is not null && !string.IsNullOrWhiteSpace(currencyChanged.Currency))
        {
            existing.Currency = currencyChanged.Currency.Trim();
            fieldsLearned++;
        }

        var invoiceChanged = rows.LastOrDefault(FieldChangeDetector.InvoiceNumberChanged);
        if (invoiceChanged is not null && !string.IsNullOrWhiteSpace(invoiceChanged.InvoiceNumber))
        {
            var invoiceHint = BuildInvoiceHintFromCorrection(invoiceChanged);
            if (!string.IsNullOrWhiteSpace(invoiceHint))
            {
                existing.InvoiceNumberHint = invoiceHint;
                fieldsLearned++;
            }
        }

        var dateChanged = rows.LastOrDefault(FieldChangeDetector.ReceiptDateChanged);
        if (dateChanged?.ReceiptDate is { } correctedDate)
        {
            var dateHint = LearnedMoneyDateHints.BuildDateHint(dateChanged, correctedDate);
            if (!string.IsNullOrWhiteSpace(dateHint))
            {
                existing.ReceiptDateHint = dateHint;
                fieldsLearned++;
            }
        }

        var subtotalChanged = rows.LastOrDefault(FieldChangeDetector.SubtotalChanged);
        if (subtotalChanged?.Subtotal is { } subtotal)
        {
            var hint = LearnedMoneyDateHints.BuildMoneyHint(subtotalChanged, subtotal);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                existing.SubtotalHint = hint;
                fieldsLearned++;
            }
        }

        var gstChanged = rows.LastOrDefault(FieldChangeDetector.GstHstChanged);
        if (gstChanged?.GstHst is { } gst)
        {
            var hint = LearnedMoneyDateHints.BuildMoneyHint(gstChanged, gst);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                existing.GstHstHint = hint;
                fieldsLearned++;
            }
        }

        var totalChanged = rows.LastOrDefault(FieldChangeDetector.TotalAmountChanged);
        if (totalChanged?.TotalAmount is { } total)
        {
            var hint = LearnedMoneyDateHints.BuildMoneyHint(totalChanged, total);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                existing.TotalAmountHint = hint;
                fieldsLearned++;
            }
        }

        existing.ModifiedAtEst = EasternTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Correction profile upserted for {Key}: store={Store}, currency={Currency}, invoiceHint={Hint}, " +
            "dateHint={DateHint}, subtotalHint={SubHint}, gstHint={GstHint}, totalHint={TotalHint}",
            similarityKey,
            existing.CanonicalStoreName,
            existing.Currency,
            existing.InvoiceNumberHint,
            existing.ReceiptDateHint,
            existing.SubtotalHint,
            existing.GstHstHint,
            existing.TotalAmountHint);
        return fieldsLearned;
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

            // Older profiles stored a one-off POS stamp (P826…). Treat it as "find this shape
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
                        // Invalid pattern — ignore; do not stamp it as a literal id.
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

        // Last resort for stable account-style ids only — never stamp a prior receipt's
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

    private static string? BuildInvoiceHintFromCorrection(ExtractedReceipt receipt)
    {
        var corrected = receipt.InvoiceNumber?.Trim();
        if (string.IsNullOrWhiteSpace(corrected))
        {
            return null;
        }

        // Per-transaction POS stamps: learn a capture pattern, not the one-off value.
        if (LooksLikePerTransactionInvoiceId(corrected))
        {
            if (Regex.IsMatch(corrected, @"^P\d{13}$", RegexOptions.IgnoreCase))
            {
                return @"\b(P\d{13})\b";
            }

            return $"label:Receipt Number;{corrected}";
        }

        var source = receipt.SourceTextPreview ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(source))
        {
            var label = DetectLabelNearValue(source, corrected);
            if (!string.IsNullOrWhiteSpace(label))
            {
                return $"label:{label};{corrected}";
            }

            if (ContainsInvoiceToken(source, corrected))
            {
                return corrected;
            }
        }

        // Still remember the corrected value so apply can use it when OCR later shows the same token
        // or when OCR only produced a weak short fragment.
        return corrected;
    }

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

    private static string? DetectLabelNearValue(string source, string value)
    {
        var escaped = Regex.Escape(value.Trim());
        var patterns = new (string Label, string Pattern)[]
        {
            ("Account Number", $@"\b(Account\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Customer Number", $@"\b(Customer\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Bill Number", $@"\b(Bill\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Invoice Number", $@"\b(Invoice\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Receipt Number", $@"\b(Receipt\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Order ID", $@"\b(Order\s*(?:ID|Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
            ("Confirmation Number", $@"\b(Confirmation\s*(?:Number|No\.?|#)?)\s*[:#]?\s*{escaped}\b"),
        };

        foreach (var (label, pattern) in patterns)
        {
            if (Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase))
            {
                return label;
            }
        }

        return null;
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

    private static void MergeAlias(ReceiptAiProfile profile, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        var aliases = ReadAliases(profile.StoreNameAliasesJson).ToList();
        var trimmed = alias.Trim();
        if (aliases.Any(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.CanonicalStoreName) &&
            string.Equals(profile.CanonicalStoreName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        aliases.Add(trimmed);
        profile.StoreNameAliasesJson = JsonSerializer.Serialize(aliases);
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

    private async Task<AiProfileDto?> AskModelForProfileAsync(
        string similarityKey,
        IReadOnlyList<ExtractedReceipt> rows,
        CancellationToken cancellationToken)
    {
        // Only rows/fields that actually changed — keeps prompt tokens low.
        var sample = rows
            .Where(FieldChangeDetector.HasAnyCorrection)
            .Take(3)
            .ToList();
        if (sample.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Receipt family key: {similarityKey}");
        sb.AppendLine("Only user-changed fields (OCR → corrected):");

        var needsOcrExcerpt = false;
        for (var i = 0; i < sample.Count; i++)
        {
            var r = sample[i];
            var changes = FieldChangeDetector.DescribeChanges(r);
            if (changes.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"--- Row {i + 1} ---");
            foreach (var line in changes)
            {
                sb.AppendLine(line);
            }

            if (FieldChangeDetector.StoreNameChanged(r) || FieldChangeDetector.InvoiceNumberChanged(r))
            {
                needsOcrExcerpt = true;
            }
        }

        // OCR text is expensive; only include a short excerpt for store/invoice learning.
        if (needsOcrExcerpt)
        {
            var withText = sample.FirstOrDefault(r =>
                (FieldChangeDetector.StoreNameChanged(r) || FieldChangeDetector.InvoiceNumberChanged(r)) &&
                !string.IsNullOrWhiteSpace(r.SourceTextPreview));
            if (withText?.SourceTextPreview is { Length: > 0 } text)
            {
                var max = Math.Min(Math.Max(400, _options.MaxSourceChars), 1200);
                if (text.Length > max)
                {
                    text = text[..max];
                }

                sb.AppendLine("Short OCR excerpt (for store/invoice label learning):");
                sb.AppendLine(text);
            }
        }

        var system = """
            You help a Canadian receipt OCR app learn from user field corrections only.
            Return ONLY compact JSON (no markdown):
            {"canonicalStoreName":"string|null","currency":"CAD|USD|null","storeNameAliases":["ocr mistakes"],"invoiceNumberHint":"label:Receipt Number|null","notes":"one short sentence"}
            Learn only from changed fields provided. Prefer vendor identity/currency.
            For invoiceNumberHint: return how to find the id on future slips.
            Grocery / Food Mart thermal receipts: prefer "label:Receipt Number" or the regex \b(P\d{13})\b — NEVER "label:Account Number".
            Utility bills may use "label:Account Number". Do not invent totals/dates.
            """;

        var body = new
        {
            model = _options.Model,
            temperature = 0.1,
            max_tokens = 250,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = sb.ToString() }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(_options.BaseUrl, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        await _usageRecorder.RecordAsync(
            operation: "correction_learning",
            model: _options.Model,
            responseJson: raw,
            success: response.IsSuccessStatusCode,
            httpStatusCode: (int)response.StatusCode,
            context: similarityKey,
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("AI learning API returned {Status}: {Body}", (int)response.StatusCode, Truncate(raw, 500));
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var json = ExtractJsonObject(content);
        var dto = JsonSerializer.Deserialize<AiProfileDto>(json, JsonOptions);
        if (dto is not null)
        {
            dto.RawResponse = Truncate(content, 2000);
        }

        return dto;
    }

    private async Task UpsertProfileAsync(string similarityKey, AiProfileDto dto, CancellationToken cancellationToken)
    {
        var existing = await _db.ReceiptAiProfiles
            .FirstOrDefaultAsync(p => p.SimilarityKey == similarityKey, cancellationToken);

        var aliasesJson = dto.StoreNameAliases is { Count: > 0 }
            ? JsonSerializer.Serialize(dto.StoreNameAliases.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            : null;

        if (existing is null)
        {
            existing = new ReceiptAiProfile
            {
                Id = Guid.NewGuid(),
                SimilarityKey = similarityKey
            };
            _db.ReceiptAiProfiles.Add(existing);
        }

        // Only overwrite profile fields the model actually returned (keeps prior learning).
        if (!string.IsNullOrWhiteSpace(dto.CanonicalStoreName))
        {
            existing.CanonicalStoreName = NormalizeOptional(dto.CanonicalStoreName);
        }

        if (!string.IsNullOrWhiteSpace(dto.Currency))
        {
            existing.Currency = NormalizeOptional(dto.Currency);
        }

        if (!string.IsNullOrWhiteSpace(aliasesJson))
        {
            existing.StoreNameAliasesJson = aliasesJson;
        }

        if (IsUsableInvoiceHint(dto.InvoiceNumberHint) &&
            !IsBadInvoiceHintForStore(dto.InvoiceNumberHint, existing.CanonicalStoreName ?? dto.CanonicalStoreName) &&
            !WouldDowngradeInvoiceHint(existing.InvoiceNumberHint, dto.InvoiceNumberHint))
        {
            existing.InvoiceNumberHint = NormalizeOptional(dto.InvoiceNumberHint);
        }

        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            existing.Notes = NormalizeOptional(dto.Notes);
        }

        existing.RawResponse = dto.RawResponse;
        existing.ModifiedAtEst = EasternTime.Now;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "AI profile upserted for {Key}: store={Store}, currency={Currency}, invoiceHint={Hint}",
            similarityKey,
            existing.CanonicalStoreName,
            existing.Currency,
            existing.InvoiceNumberHint);
    }

    private static bool IsBadInvoiceHintForStore(string? hint, string? storeName)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        var isAccount = hint.Contains("Account Number", StringComparison.OrdinalIgnoreCase);
        if (!isAccount)
        {
            return false;
        }

        var store = storeName ?? string.Empty;
        return store.Contains("Food Mart", StringComparison.OrdinalIgnoreCase) ||
               (store.Contains("Premium", StringComparison.OrdinalIgnoreCase) &&
                !store.Contains("Hydro", StringComparison.OrdinalIgnoreCase));
    }

    private static bool WouldDowngradeInvoiceHint(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(existing) || string.IsNullOrWhiteSpace(incoming))
        {
            return false;
        }

        var existingIsPos =
            existing.Contains(@"P\d", StringComparison.OrdinalIgnoreCase) ||
            existing.Contains("Receipt Number", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(existing, @"P\d{10,}", RegexOptions.IgnoreCase);
        var incomingIsAccount = incoming.Contains("Account Number", StringComparison.OrdinalIgnoreCase);
        return existingIsPos && incomingIsAccount;
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        return $"{baseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";
    }

    private static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, @"^```(?:json)?\s*", string.Empty, RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"\s*```$", string.Empty);
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class AiProfileDto
    {
        public string? CanonicalStoreName { get; set; }
        public string? Currency { get; set; }
        public List<string>? StoreNameAliases { get; set; }
        public string? InvoiceNumberHint { get; set; }
        public string? Notes { get; set; }

        [JsonIgnore]
        public string? RawResponse { get; set; }
    }
}
