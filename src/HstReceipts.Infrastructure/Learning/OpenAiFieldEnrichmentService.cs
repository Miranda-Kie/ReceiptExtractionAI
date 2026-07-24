using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Learning;

/// <summary>
/// Structured LLM fill for missing receipt fields after rule-based extraction.
/// Proposals are validated locally against OCR before apply.
/// </summary>
public sealed class OpenAiFieldEnrichmentService : IAiFieldEnrichmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AiLearningOptions _options;
    private readonly IOpenAiUsageRecorder _usageRecorder;
    private readonly ILogger<OpenAiFieldEnrichmentService> _logger;

    public OpenAiFieldEnrichmentService(
        HttpClient http,
        IOptions<AiLearningOptions> options,
        IOpenAiUsageRecorder usageRecorder,
        ILogger<OpenAiFieldEnrichmentService> logger)
    {
        _http = http;
        _options = options.Value;
        _usageRecorder = usageRecorder;
        _logger = logger;
    }

    public bool IsEnabled =>
        _options.FillMissingFields &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.Model);

    public async Task EnrichMissingFieldsAsync(
        IList<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || receipts.Count == 0)
        {
            return;
        }

        var candidates = receipts
            .Where(r => r.Success || !string.IsNullOrWhiteSpace(r.SourceTextPreview))
            .Where(LlmFieldProposalValidator.NeedsEnrichment)
            .Take(Math.Clamp(_options.MaxFillPerBatch, 1, 100))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var filledReceipts = 0;
        foreach (var receipt in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await _usageRecorder.TryAcquireAsync("field_fill", receipt.ReceiptName, cancellationToken))
            {
                receipt.Warnings.Add("AI field fill skipped: daily OpenAI usage limit reached for your account.");
                break;
            }

            try
            {
                var proposal = await AskModelAsync(receipt, cancellationToken);
                if (proposal is null)
                {
                    continue;
                }

                var n = LlmFieldProposalValidator.ApplyValidated(
                    receipt,
                    proposal,
                    receipt.SourceTextPreview ?? string.Empty);
                if (n > 0)
                {
                    filledReceipts++;
                    _logger.LogInformation(
                        "AI field fill applied {Count} field(s) for {Name}. Evidence={Evidence}",
                        n,
                        receipt.ReceiptName,
                        Truncate(proposal.Evidence, 120));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI field fill failed for {Name}.", receipt.ReceiptName);
            }
        }

        if (filledReceipts > 0)
        {
            _logger.LogInformation("AI field fill enriched {Count} receipt(s).", filledReceipts);
        }
    }

    private async Task<LlmFieldProposal?> AskModelAsync(
        ExtractedReceipt receipt,
        CancellationToken cancellationToken)
    {
        var missing = LlmFieldProposalValidator.ListMissingFields(receipt);
        if (missing.Count == 0)
        {
            return null;
        }

        var ocr = receipt.SourceTextPreview ?? string.Empty;
        var max = Math.Clamp(_options.MaxSourceChars, 400, 6000);
        if (ocr.Length > max)
        {
            ocr = ocr[..max];
        }

        var user = new StringBuilder();
        user.AppendLine($"Receipt file: {receipt.ReceiptName}");
        user.AppendLine($"Missing fields to fill (only these): {string.Join(", ", missing)}");
        user.AppendLine("Current values (do not invent replacements for non-null fields):");
        user.AppendLine($"storeName={Fmt(receipt.StoreName)}; invoiceNumber={Fmt(receipt.InvoiceNumber)}; currency={Fmt(receipt.Currency)}");
        user.AppendLine($"receiptDate={FmtDate(receipt.ReceiptDate)}; transactionTime={Fmt(receipt.TransactionTime)}");
        user.AppendLine($"subtotal={FmtMoney(receipt.Subtotal)}; gstHst={FmtMoney(receipt.GstHst)}; totalAmount={FmtMoney(receipt.TotalAmount)}");
        user.AppendLine("OCR text:");
        user.AppendLine(ocr);

        var system = """
            You extract Canadian receipt fields from OCR text for a bookkeeping app.
            Return ONLY compact JSON (no markdown):
            {"storeName":"string|null","invoiceNumber":"string|null","currency":"CAD|USD|null","receiptDate":"yyyy-MM-dd|null","transactionTime":"HH:mm:ss|null","subtotal":"0.00|null","gstHst":"0.00|null","totalAmount":"0.00|null","evidence":"short label list"}
            Rules:
            - Only fill fields listed as missing. Leave others null.
            - Values MUST appear in the OCR (or be a clear OCR typo of text that appears). Never invent totals or invoice ids.
            - Prefer labeled values (Invoice/Receipt #, HST, Total after tax, Sub Total).
            - currency defaults to CAD when HST/GST/$ appears.
            - evidence: brief phrase of labels used (e.g. "Receipt stamp P8…; HST; Total after tax").
            """;

        var body = new
        {
            model = _options.Model,
            temperature = 0,
            max_tokens = 350,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user.ToString() }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(_options.BaseUrl, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        await _usageRecorder.RecordAsync(
            operation: "field_fill",
            model: _options.Model,
            responseJson: raw,
            success: response.IsSuccessStatusCode,
            httpStatusCode: (int)response.StatusCode,
            context: receipt.ReceiptName,
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AI field fill API returned {Status}: {Body}",
                (int)response.StatusCode,
                Truncate(raw, 400));
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
        return JsonSerializer.Deserialize<LlmFieldProposal>(json, JsonOptions);
    }

    private static string CombineUrl(string baseUrl, string relative)
    {
        var b = baseUrl.TrimEnd('/');
        var r = relative.TrimStart('/');
        return $"{b}/{r}";
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

    private static string Fmt(string? value)
        => string.IsNullOrWhiteSpace(value) ? "null" : value.Trim();

    private static string FmtDate(DateOnly? value)
        => value is null ? "null" : value.Value.ToString("yyyy-MM-dd");

    private static string FmtMoney(decimal? value)
        => value is null ? "null" : value.Value.ToString("0.00");

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value ?? string.Empty;
        }

        return value[..max] + "…";
    }
}
