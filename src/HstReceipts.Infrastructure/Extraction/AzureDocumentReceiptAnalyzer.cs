using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace HstReceipts.Infrastructure.Extraction;

public sealed class AzureDocumentReceiptAnalyzer : IDocumentReceiptAnalyzer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp", ".pdf"
    };

    private readonly DocumentIntelligenceClient? _client;
    private readonly DocumentIntelligenceOptions _options;
    private readonly IReceiptFieldExtractor _fieldExtractor;
    private readonly ILogger<AzureDocumentReceiptAnalyzer> _logger;

    private readonly RoleDocumentIntelligence? _roleOptions;

    public AzureDocumentReceiptAnalyzer(
        IOptions<DocumentIntelligenceOptions> options,
        IReceiptFieldExtractor fieldExtractor,
        ILogger<AzureDocumentReceiptAnalyzer> logger)
    {
        _options = options.Value;
        _fieldExtractor = fieldExtractor;
        _logger = logger;

        // Prefer Owner config, fall back to Admin config
        _roleOptions = _options.Owner.IsConfigured ? _options.Owner
            : _options.Admin.IsConfigured ? _options.Admin
            : null;

        if (_roleOptions?.IsConfigured == true)
        {
            _client = new DocumentIntelligenceClient(
                new Uri(_roleOptions.Endpoint.TrimEnd('/') + "/"),
                new AzureKeyCredential(_roleOptions.ApiKey.Trim()));
        }
    }

    public bool IsAvailable => _client is not null;

    public bool CanHandle(string fileName) =>
        IsAvailable && SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<IReadOnlyList<ExtractedReceipt>> AnalyzeAsync(
        Stream stream,
        string receiptLabel,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Azure Document Intelligence is not configured.");
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var pdfBytes = buffer.ToArray();
        var bytes = BinaryData.FromBytes(pdfBytes);

        var analyzeOptions = new AnalyzeDocumentOptions(_roleOptions!.ModelId, bytes);
        if (!string.IsNullOrWhiteSpace(_roleOptions!.Locale))
        {
            analyzeOptions.Locale = _roleOptions!.Locale;
        }

        _logger.LogInformation(
            "Analyzing {Receipt} with Azure Document Intelligence model {Model}",
            receiptLabel,
            _roleOptions!.ModelId);

        Operation<AnalyzeResult> operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            analyzeOptions,
            cancellationToken);

        var result = operation.Value;
        var pdfPageCount = TryGetPdfPageCount(pdfBytes);
        var diPageCount = result.Pages?.Count ?? 0;
        var content = BuildPagedContent(result);

        // If PdfPig sees more pages than DI, pull per-page OCR text for rule splitting.
        if (pdfPageCount >= 2 &&
            (pdfPageCount > diPageCount || pdfPageCount > (result.Documents?.Count ?? 0)))
        {
            _logger.LogInformation(
                "Enriching {Receipt} with per-page OCR (PDF pages={PdfPages}, DI pages={DiPages}, DI docs={DiDocs})",
                receiptLabel,
                pdfPageCount,
                diPageCount,
                result.Documents?.Count ?? 0);
            var perPageText = await GetPerPageOcrTextAsync(bytes, pdfPageCount, cancellationToken);
            if (perPageText.Count > content.Split('\f').Length)
            {
                content = string.Join("\f", perPageText);
            }
            else if (perPageText.Count >= pdfPageCount && perPageText.Count > 1)
            {
                content = string.Join("\f", perPageText);
            }
        }

        var pageTexts = content.Split('\f', StringSplitOptions.None)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        var preview = content.Length > 4000 ? content[..4000] : content;

        var rows = new List<ExtractedReceipt>();
        if (result.Documents is { Count: > 0 })
        {
            var index = 0;
            foreach (var document in result.Documents)
            {
                index++;
                var label = result.Documents.Count == 1
                    ? receiptLabel
                    : $"{receiptLabel}#{index}";
                // Scope preview to this page when possible so invoice finalize cannot copy another slip's POS stamp.
                var pagePreview = index <= pageTexts.Count
                    ? TruncatePreview(pageTexts[index - 1])
                    : preview;
                rows.Add(MapDocument(document, label, pagePreview));
            }
        }
        else if (!string.IsNullOrWhiteSpace(content))
        {
            rows.AddRange(_fieldExtractor.ExtractAll(content, receiptLabel));
            foreach (var row in rows)
            {
                row.SourceTextPreview ??= preview;
            }
        }
        else
        {
            rows.Add(new ExtractedReceipt
            {
                ReceiptName = receiptLabel,
                Success = false,
                ErrorMessage = "Document Intelligence returned no content.",
                Warnings = { "Azure Document Intelligence returned empty result." }
            });
        }

        if (_roleOptions!.FillGapsWithRules && !string.IsNullOrWhiteSpace(content))
        {
            rows = PreferRuleHybrid(rows, content, receiptLabel);
        }

        // Last-resort fallback: whole-document analysis and rule-based splitting both under-counted
        // relative to the PDF's page count (e.g. each page is a distinct receipt DI merged into one
        // document). Re-analyze page-by-page through Document Intelligence and keep it only if it
        // finds more usable receipts than what we already have.
        if (pdfPageCount >= 2 && rows.Count(IsUsablePreviewRow) < pdfPageCount)
        {
            var perPageRows = await AnalyzePerPageStructuredAsync(bytes, pdfPageCount, receiptLabel, cancellationToken);
            var usablePerPage = perPageRows.Count(IsUsablePreviewRow);
            var usableSoFar = rows.Count(IsUsablePreviewRow);
            if (usablePerPage > usableSoFar)
            {
                _logger.LogInformation(
                    "Per-page structured re-analysis found {PerPageCount} usable receipt(s) vs {ExistingCount} for {Receipt} (pdfPages={PdfPages}); using per-page results.",
                    usablePerPage,
                    usableSoFar,
                    receiptLabel,
                    pdfPageCount);
                rows = perPageRows;
            }
        }

        foreach (var row in rows)
        {
            // Do not replace page-scoped preview with the full multi-receipt PDF text.
            row.SourceTextPreview ??= preview;
            ReceiptFieldExtractor.FinalizeAiPremiumFoodMartRow(row);
            ExtractedReceiptValidator.Apply(row);
        }

        _logger.LogInformation(
            "Document Intelligence hybrid produced {Count} receipt row(s) for {Receipt} (DI docs={DiDocs}, pages={Pages}, pdfPages={PdfPages})",
            rows.Count,
            receiptLabel,
            result.Documents?.Count ?? 0,
            result.Pages?.Count ?? 0,
            pdfPageCount);

        if (pdfPageCount <= 1 && rows.Count(IsUsablePreviewRow) <= 1)
        {
            // Single-page/image PDFs rely entirely on Document Intelligence's own multi-object
            // detection (result.Documents). Nothing more we can split on our side — log so a
            // genuinely multi-receipt single-page upload that under-extracts is diagnosable.
            _logger.LogInformation(
                "{Receipt} is single-page and produced 1 receipt; if the source image contains multiple receipts, this depends on Document Intelligence's multi-object detection.",
                receiptLabel);
        }

        return rows;
    }

    /// <summary>
    /// Re-analyzes each PDF page individually through Document Intelligence and maps each page's
    /// structured document as its own receipt. Used as a fallback when whole-document analysis
    /// merges distinct per-page receipts into fewer documents than the PDF actually has pages.
    /// Costs one extra Document Intelligence call per page — only invoked when a shortfall is detected.
    /// </summary>
    private async Task<List<ExtractedReceipt>> AnalyzePerPageStructuredAsync(
        BinaryData bytes,
        int pageCount,
        string receiptLabel,
        CancellationToken cancellationToken)
    {
        var results = new List<ExtractedReceipt>();
        if (_client is null || pageCount < 2)
        {
            return results;
        }

        for (var page = 1; page <= pageCount; page++)
        {
            var pageOptions = new AnalyzeDocumentOptions(_roleOptions!.ModelId, bytes);
            if (!string.IsNullOrWhiteSpace(_roleOptions!.Locale))
            {
                pageOptions.Locale = _roleOptions!.Locale;
            }

            pageOptions.Pages = page.ToString(CultureInfo.InvariantCulture);

            try
            {
                Operation<AnalyzeResult> op = await _client.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    pageOptions,
                    cancellationToken);
                var pageResult = op.Value;
                var pageContent = TruncatePreview((pageResult.Content ?? string.Empty).Trim());

                if (pageResult.Documents is { Count: > 0 })
                {
                    var docIndex = 0;
                    foreach (var document in pageResult.Documents)
                    {
                        docIndex++;
                        var label = pageCount == 1 && pageResult.Documents.Count == 1
                            ? receiptLabel
                            : pageResult.Documents.Count > 1
                                ? $"{receiptLabel}#p{page}-{docIndex}"
                                : $"{receiptLabel}#p{page}";
                        results.Add(MapDocument(document, label, pageContent));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-page structured Document Intelligence failed for page {Page} of {Receipt}", page, receiptLabel);
            }
        }

        foreach (var row in results)
        {
            ReceiptFieldExtractor.FinalizeAiPremiumFoodMartRow(row);
            ExtractedReceiptValidator.Apply(row);
        }

        return results;
    }

    private async Task<List<string>> GetPerPageOcrTextAsync(
        BinaryData bytes,
        int pageCount,
        CancellationToken cancellationToken)
    {
        var pageContents = new List<string>();
        if (_client is null || pageCount < 2)
        {
            return pageContents;
        }

        for (var page = 1; page <= pageCount; page++)
        {
            var pageOptions = new AnalyzeDocumentOptions(_roleOptions!.ModelId, bytes);
            if (!string.IsNullOrWhiteSpace(_roleOptions!.Locale))
            {
                pageOptions.Locale = _roleOptions!.Locale;
            }

            pageOptions.Pages = page.ToString(CultureInfo.InvariantCulture);

            try
            {
                Operation<AnalyzeResult> op = await _client.AnalyzeDocumentAsync(
                    WaitUntil.Completed,
                    pageOptions,
                    cancellationToken);
                var pageText = (op.Value.Content ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    pageContents.Add(pageText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Per-page Document Intelligence failed for page {Page}", page);
            }
        }

        return pageContents;
    }

    private static int TryGetPdfPageCount(byte[] pdfBytes)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            return doc.NumberOfPages;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Rebuild OCR text with a form-feed between PDF pages so multi-slip extractors can split.
    /// </summary>
    private static string BuildPagedContent(AnalyzeResult result)
    {
        var full = result.Content ?? string.Empty;
        if (result.Pages is not { Count: > 1 } || string.IsNullOrEmpty(full))
        {
            return full;
        }

        var parts = new List<string>(result.Pages.Count);
        foreach (var page in result.Pages.OrderBy(p => p.PageNumber))
        {
            if (page.Spans is { Count: > 0 })
            {
                var sb = new StringBuilder();
                foreach (var span in page.Spans)
                {
                    if (span.Offset < 0 || span.Length < 0 || span.Offset + span.Length > full.Length)
                    {
                        continue;
                    }

                    sb.Append(full.AsSpan(span.Offset, span.Length));
                }

                var pageText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    parts.Add(pageText);
                }

                continue;
            }

            if (page.Lines is { Count: > 0 })
            {
                var pageText = string.Join('\n', page.Lines.Select(l => l.Content)).Trim();
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    parts.Add(pageText);
                }
            }
        }

        return parts.Count > 1 ? string.Join("\f", parts) : full;
    }

    private static string TruncatePreview(string text) =>
        text.Length > 4000 ? text[..4000] : text;

    private List<ExtractedReceipt> PreferRuleHybrid(
        List<ExtractedReceipt> diRows,
        string content,
        string receiptLabel)
    {
        var ruleRows = _fieldExtractor.ExtractAll(content, receiptLabel).ToList();
        if (ruleRows.Count == 0)
        {
            return diRows;
        }

        // Multi-page / multi-slip PDFs: prefer rules only when they find more *usable* receipts.
        var qualityRules = ruleRows.Count(IsUsablePreviewRow);
        var qualityDi = diRows.Count(IsUsablePreviewRow);
        if (qualityRules > qualityDi && qualityRules >= diRows.Count)
        {
            _logger.LogInformation(
                "Preferring rule extraction ({RuleQuality}/{RuleCount} usable) over Document Intelligence ({DiQuality}/{DiCount}) for {Receipt}",
                qualityRules,
                ruleRows.Count,
                qualityDi,
                diRows.Count,
                receiptLabel);
            var preferred = ruleRows.Where(IsUsablePreviewRow).ToList();
            MergeDiTransactionTimesOntoRules(diRows, preferred);
            return preferred;
        }

        if (ruleRows.Count > diRows.Count && qualityRules <= qualityDi)
        {
            _logger.LogInformation(
                "Ignoring over-split rule rows ({RuleCount} total, {RuleQuality} usable) for {Receipt}; keeping DI/hybrid merge",
                ruleRows.Count,
                qualityRules,
                receiptLabel);
        }

        // Same usable count, but rules recovered more distinct POS invoices — prefer rules.
        var ruleInvoices = ruleRows
            .Where(IsUsablePreviewRow)
            .Select(r => r.InvoiceNumber)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var diInvoices = diRows
            .Where(IsUsablePreviewRow)
            .Select(r => r.InvoiceNumber)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (qualityRules >= qualityDi && ruleInvoices > diInvoices)
        {
            _logger.LogInformation(
                "Preferring rule extraction for {Receipt}: {RuleInvoices} distinct invoices vs DI {DiInvoices}",
                receiptLabel,
                ruleInvoices,
                diInvoices);
            var preferred = ruleRows.Where(IsUsablePreviewRow).ToList();
            MergeDiTransactionTimesOntoRules(diRows, preferred);
            return preferred;
        }

        if (diRows.Count == 1 && ruleRows.Count == 1)
        {
            MergePreferringRulesWhenBetter(diRows[0], ruleRows[0]);
            return diRows;
        }

        var count = Math.Min(diRows.Count, ruleRows.Count);
        for (var i = 0; i < count; i++)
        {
            MergePreferringRulesWhenBetter(diRows[i], ruleRows[i]);
        }

        return diRows;
    }

    private static bool IsUsablePreviewRow(ExtractedReceipt row) =>
        row.TotalAmount is >= 5m ||
        (!string.IsNullOrWhiteSpace(row.InvoiceNumber) &&
         row.ReceiptDate is not null &&
         (row.Subtotal is >= 5m || row.TotalAmount is not null));

    /// <summary>
    /// When rules win on slip count, keep DI TransactionTime (from TRANSACTION RECORD) if the
    /// rule row still only has a POS-stamp time / no card-block text.
    /// </summary>
    private static void MergeDiTransactionTimesOntoRules(
        List<ExtractedReceipt> diRows,
        List<ExtractedReceipt> ruleRows)
    {
        foreach (var rule in ruleRows)
        {
            var preview = rule.SourceTextPreview ?? string.Empty;
            var hasTransactionRecord = Regex.IsMatch(
                preview,
                @"TRANSACTION\s*RECORD",
                RegexOptions.IgnoreCase);
            if (hasTransactionRecord && !string.IsNullOrWhiteSpace(rule.TransactionTime))
            {
                continue;
            }

            ExtractedReceipt? match = null;
            foreach (var di in diRows)
            {
                if (string.IsNullOrWhiteSpace(di.TransactionTime))
                {
                    continue;
                }

                if (rule.ReceiptDate is not null &&
                    di.ReceiptDate is not null &&
                    rule.ReceiptDate != di.ReceiptDate)
                {
                    continue;
                }

                if (rule.TotalAmount is not null &&
                    di.TotalAmount is not null &&
                    Math.Abs(rule.TotalAmount.Value - di.TotalAmount.Value) > 0.05m)
                {
                    continue;
                }

                match = di;
                break;
            }

            if (match is not null)
            {
                rule.TransactionTime = match.TransactionTime;
            }
        }
    }

    /// <summary>
    /// Fill nulls from rules; replace money/invoice when DI values fail authentication or are blank.
    /// </summary>
    private static void MergePreferringRulesWhenBetter(ExtractedReceipt target, ExtractedReceipt source)
    {
        target.StoreName ??= source.StoreName;
        target.Currency ??= source.Currency;
        // Keep DI TransactionTime (card TRANSACTION RECORD) over empty/POS-only rule fills.
        if (string.IsNullOrWhiteSpace(target.TransactionTime))
        {
            target.TransactionTime = source.TransactionTime;
        }

        target.ReceiptDate ??= source.ReceiptDate;

        if (string.IsNullOrWhiteSpace(target.InvoiceNumber)
            && !string.IsNullOrWhiteSpace(source.InvoiceNumber))
        {
            target.InvoiceNumber = source.InvoiceNumber;
        }

        var diAmountsBroken = AmountsBroken(target);
        var ruleAmountsOk = !AmountsBroken(source)
            && source.Subtotal is not null
            && source.TotalAmount is not null;

        if (diAmountsBroken && ruleAmountsOk)
        {
            target.Subtotal = source.Subtotal;
            target.GstHst = source.GstHst;
            target.TotalAmount = source.TotalAmount;
            target.Warnings.Add("Amount fields replaced with store-rule values (Document Intelligence amounts failed checks).");
            return;
        }

        target.Subtotal ??= source.Subtotal;
        target.GstHst ??= source.GstHst;
        target.TotalAmount ??= source.TotalAmount;

        if (target.GstHst is null && source.GstHst is not null)
        {
            target.GstHst = source.GstHst;
        }
    }

    private static bool AmountsBroken(ExtractedReceipt row)
    {
        if (row.Subtotal is null || row.TotalAmount is null)
        {
            return true;
        }

        if (row.GstHst is null)
        {
            return true;
        }

        var expected = decimal.Round(row.Subtotal.Value + row.GstHst.Value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(expected - row.TotalAmount.Value) > 0.02m;
    }

    /// <summary>
    /// Fallback for receipts where Document Intelligence's own InvoiceId/ReceiptNumber/TransactionId
    /// fields miss the order/invoice number (e.g. Walmart's "Order# 6000000-75448351" isn't
    /// recognized by the prebuilt-receipt model's field ontology).
    /// </summary>
    private static readonly Regex OrderNumberPattern = new(
        @"(?:Order|Invoice|Receipt|Transaction)\s*#\s*:?\s*([A-Za-z0-9][A-Za-z0-9\-]{4,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? ExtractOrderNumberFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = OrderNumberPattern.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static ExtractedReceipt MapDocument(
        AnalyzedDocument document,
        string receiptLabel,
        string preview)
    {
        var fields = document.Fields;
        var row = new ExtractedReceipt
        {
            ReceiptName = receiptLabel,
            Success = true,
            SourceTextPreview = preview,
            StoreName = GetString(fields, "MerchantName"),
            InvoiceNumber = FirstString(fields, "InvoiceId", "ReceiptNumber", "TransactionId")
                ?? ExtractOrderNumberFromText(preview),
            TransactionTime = GetTime(fields, "TransactionTime"),
            ReceiptDate = GetDate(fields, "TransactionDate"),
            Subtotal = GetMoney(fields, "Subtotal"),
            GstHst = GetMoney(fields, "TotalTax") ?? GetMoney(fields, "Tax"),
            TotalAmount = GetMoney(fields, "Total"),
            Currency = GetCurrencyCode(fields, "Total")
                ?? GetCurrencyCode(fields, "Subtotal")
                ?? GetString(fields, "CurrencyCode")
        };

        if (string.IsNullOrWhiteSpace(row.StoreName)
            && string.IsNullOrWhiteSpace(row.InvoiceNumber)
            && row.TotalAmount is null
            && row.GstHst is null
            && row.ReceiptDate is null)
        {
            row.Warnings.Add("Document Intelligence did not extract key receipt fields.");
        }

        return row;
    }

    private static string? FirstString(
        IReadOnlyDictionary<string, DocumentField> fields,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(fields, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetString(
        IReadOnlyDictionary<string, DocumentField> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var field) || field is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(field.ValueString))
        {
            return field.ValueString.Trim();
        }

        return string.IsNullOrWhiteSpace(field.Content) ? null : field.Content.Trim();
    }

    private static DateOnly? GetDate(
        IReadOnlyDictionary<string, DocumentField> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var field) || field is null)
        {
            return null;
        }

        if (field.ValueDate is DateTimeOffset dto)
        {
            return DateOnly.FromDateTime(dto.Date);
        }

        if (!string.IsNullOrWhiteSpace(field.Content)
            && DateTime.TryParse(field.Content, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return DateOnly.FromDateTime(parsed);
        }

        return null;
    }

    private static string? GetTime(
        IReadOnlyDictionary<string, DocumentField> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var field) || field is null)
        {
            return null;
        }

        if (field.ValueTime is TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(field.Content) ? null : field.Content.Trim();
    }

    private static decimal? GetMoney(
        IReadOnlyDictionary<string, DocumentField> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var field) || field is null)
        {
            return null;
        }

        if (field.ValueCurrency is { } currency)
        {
            return Convert.ToDecimal(currency.Amount, CultureInfo.InvariantCulture);
        }

        if (field.ValueDouble is double d)
        {
            return Convert.ToDecimal(d, CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(field.Content)
            && decimal.TryParse(
                field.Content.Replace("$", string.Empty).Trim(),
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? GetCurrencyCode(
        IReadOnlyDictionary<string, DocumentField> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var field) || field?.ValueCurrency is null)
        {
            return null;
        }

        var code = field.ValueCurrency.CurrencyCode;
        return string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    }
}
