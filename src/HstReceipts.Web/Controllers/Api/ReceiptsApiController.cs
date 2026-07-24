using System.Text.Json;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using HstReceipts.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HstReceipts.Web.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/receipts")]
public sealed class ReceiptsApiController : ControllerBase
{
    private static readonly JsonSerializerOptions SessionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string AiLearningSessionKey = "AiLearningEnabled";
    private const string BatchSessionKey = "CurrentBatch";

    private readonly IReceiptProcessingService _processingService;
    private readonly IReceiptRepository _repository;
    private readonly IExcelExportService _excelExportService;
    private readonly IAiCorrectionLearningService _aiLearning;
    private readonly ReceiptProcessingOptions _options;
    private readonly ILogger<ReceiptsApiController> _logger;

    public ReceiptsApiController(
        IReceiptProcessingService processingService,
        IReceiptRepository repository,
        IExcelExportService excelExportService,
        IAiCorrectionLearningService aiLearning,
        IOptions<ReceiptProcessingOptions> options,
        ILogger<ReceiptsApiController> logger)
    {
        _processingService = processingService;
        _repository = repository;
        _excelExportService = excelExportService;
        _aiLearning = aiLearning;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("session")]
    public IActionResult GetSession()
    {
        var batch = GetBatchFromSession();
        return Ok(new
        {
            batchId = batch?.BatchId,
            receipts = batch?.Receipts ?? [],
            aiLearning = new
            {
                showToggle = User.IsInRole(AppRoles.Admin),
                configured = _aiLearning.IsEnabled,
                enabled = IsAiLearningSessionEnabled()
            }
        });
    }

    [HttpPost("ai-learning")]
    [Authorize(Roles = AppRoles.Admin)]
    public IActionResult SetAiLearning([FromBody] AiLearningRequest request)
    {
        HttpContext.Session.SetString(AiLearningSessionKey, request.Enabled ? "1" : "0");
        return Ok(new { enabled = request.Enabled });
    }

    public sealed class AiLearningRequest
    {
        public bool Enabled { get; set; }
    }

    [HttpPost("process")]
    [RequestSizeLimit(104_857_600)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> Process(List<IFormFile>? files, CancellationToken cancellationToken)
    {
        var uploads = new List<UploadedReceiptFile>();
        try
        {
            if (files is null || files.Count == 0)
            {
                return BadRequest(new { error = "Upload receipt files to continue." });
            }

            var totalBytes = files.Sum(f => f.Length);
            if (totalBytes > _options.MaxUploadBytes)
            {
                return BadRequest(new
                {
                    error = $"Upload exceeds the maximum size of {_options.MaxUploadBytes / (1024 * 1024)} MB."
                });
            }

            foreach (var file in files)
            {
                var memory = new MemoryStream();
                await file.CopyToAsync(memory, cancellationToken);
                memory.Position = 0;
                uploads.Add(new UploadedReceiptFile
                {
                    FileName = file.FileName,
                    Content = memory
                });
            }

            var batch = await _processingService.ProcessUploadsAsync(uploads, cancellationToken);
            StoreBatchInSession(batch);

            return Ok(new
            {
                batchId = batch.BatchId,
                receipts = batch.Receipts,
                message = $"Processed {uploads.Count} file(s) into {batch.Receipts.Count} receipt(s).",
                aiLearning = new
                {
                    showToggle = User.IsInRole(AppRoles.Admin),
                    configured = _aiLearning.IsEnabled,
                    enabled = IsAiLearningSessionEnabled()
                }
            });
        }
        finally
        {
            foreach (var upload in uploads)
            {
                await upload.Content.DisposeAsync();
            }
        }
    }

    public sealed class PreviewBatchRequest
    {
        public Guid BatchId { get; set; }
        public List<ReceiptFieldEdit>? Receipts { get; set; }
        public List<string>? ExportFields { get; set; }

        /// <summary>Required for export-save — user confirmed preview fields (especially invoice) are correct.</summary>
        public bool PreviewValidated { get; set; }
    }

    [HttpPost("compare-export")]
    public async Task<IActionResult> CompareForExport(
        [FromBody] PreviewBatchRequest request,
        CancellationToken cancellationToken)
    {
        var batch = GetBatchFromSession();
        if (batch is null || batch.BatchId != request.BatchId)
        {
            return BadRequest(new { error = "No processed batch found. Please upload receipts again." });
        }

        ApplyPreviewEdits(batch, request.Receipts);
        StoreBatchInSession(batch);

        if (TryGetMissingRequiredFieldError(batch.Receipts, out var requiredError))
        {
            return BadRequest(new { error = requiredError });
        }

        var result = new ExportCompareResult();
        foreach (var preview in batch.Receipts)
        {
            if (string.IsNullOrWhiteSpace(preview.InvoiceNumber) ||
                string.IsNullOrWhiteSpace(preview.StoreName) ||
                string.IsNullOrWhiteSpace(preview.Currency) ||
                preview.ReceiptDate is null)
            {
                result.SkippedCount++;
                continue;
            }

            var (existing, matchKind) = await _repository.FindMatchAsync(
                preview.InvoiceNumber,
                cancellationToken);

            if (existing is null)
            {
                result.NewCount++;
                continue;
            }

            var diffs = BuildFieldDifferences(existing, preview);
            if (diffs.Count == 0)
            {
                result.UnchangedCount++;
                continue;
            }

            result.Conflicts.Add(new ExportConflictDto
            {
                StoreName = preview.StoreName,
                InvoiceNumber = preview.InvoiceNumber,
                ReceiptDate = preview.ReceiptDate?.ToString("yyyy-MM-dd"),
                MatchKind = matchKind,
                SameDateMatch = string.Equals(
                    existing.ReceiptDate.ToString("yyyy-MM-dd"),
                    preview.ReceiptDate?.ToString("yyyy-MM-dd"),
                    StringComparison.Ordinal),
                Differences = diffs
            });
        }

        return Ok(result);
    }

    [HttpPost("export-save")]
    public async Task<IActionResult> ExportAndSave(
        [FromBody] PreviewBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Demo))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Demo mode cannot save to the database. Use Export Excel only."
            });
        }

        if (!request.PreviewValidated)
        {
            return BadRequest(new
            {
                error = "Confirm the preview validation checkbox before saving to the database."
            });
        }

        var batch = GetBatchFromSession();
        if (batch is null || batch.BatchId != request.BatchId)
        {
            return BadRequest(new { error = "No processed batch found. Please upload receipts again." });
        }

        ApplyPreviewEdits(batch, request.Receipts);
        StoreBatchInSession(batch);

        if (TryGetMissingRequiredFieldError(batch.Receipts, out var requiredError))
        {
            return BadRequest(new { error = requiredError });
        }

        int inserted;
        int updated;
        int skipped;
        int corrections;
        try
        {
            (inserted, updated, skipped, corrections) = await _repository.UpsertWithCorrectionsAsync(
                batch.BatchId,
                batch.Receipts,
                User.Identity?.Name ?? "unknown",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert receipts during API export.");
            return StatusCode(500, new { error = "Could not save receipts to the database during export." });
        }

        var learningMessage = "AI learning skipped (not admin or toggle off).";
        if (User.IsInRole(AppRoles.Admin) && IsAiLearningSessionEnabled())
        {
            try
            {
                var learning = await _aiLearning.LearnFromCorrectedReceiptsAsync(batch.Receipts, cancellationToken);
                learningMessage = learning.Message;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI learning after API export failed.");
                learningMessage = "AI learning failed — see server logs.";
            }
        }

        var columns = ExcelExportColumns.FromSelected(request.ExportFields);
        var bytes = _excelExportService.Export(batch.Receipts, columns);
        ClearBatchFromSession();
        Response.Headers["X-Ai-Learning-Result"] = Uri.EscapeDataString(learningMessage);
        Response.Headers["X-Save-Result"] = Uri.EscapeDataString(
            $"inserted={inserted};updated={updated};skipped={skipped};corrections={corrections}");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "receipts.xlsx");
    }

    [HttpPost("export-only")]
    public IActionResult ExportOnly([FromBody] PreviewBatchRequest request)
    {
        var batch = GetBatchFromSession();
        if (batch is null || batch.BatchId != request.BatchId)
        {
            return BadRequest(new { error = "No processed batch found. Please upload receipts again." });
        }

        ApplyPreviewEdits(batch, request.Receipts);

        if (TryGetMissingRequiredFieldError(batch.Receipts, out var requiredError))
        {
            return BadRequest(new { error = requiredError });
        }

        var columns = ExcelExportColumns.FromSelected(request.ExportFields);
        var bytes = _excelExportService.Export(batch.Receipts, columns);
        ClearBatchFromSession();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "receipts.xlsx");
    }

    private ReceiptBatchResult? GetBatchFromSession()
    {
        var json = HttpContext.Session.GetString(BatchSessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReceiptBatchResult>(json, SessionJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void StoreBatchInSession(ReceiptBatchResult batch)
    {
        HttpContext.Session.SetString(BatchSessionKey, JsonSerializer.Serialize(batch, SessionJsonOptions));
    }

    private void ClearBatchFromSession()
    {
        HttpContext.Session.Remove(BatchSessionKey);
    }

    private bool IsAiLearningSessionEnabled() =>
        string.Equals(HttpContext.Session.GetString(AiLearningSessionKey), "1", StringComparison.Ordinal);

    private static void ApplyPreviewEdits(ReceiptBatchResult batch, List<ReceiptFieldEdit>? edits)
    {
        if (edits is null || edits.Count == 0)
        {
            return;
        }

        var count = Math.Min(batch.Receipts.Count, edits.Count);
        for (var i = 0; i < count; i++)
        {
            var target = batch.Receipts[i];
            var edit = edits[i];
            target.StoreName = string.IsNullOrWhiteSpace(edit.StoreName) ? null : edit.StoreName.Trim();
            target.InvoiceNumber = string.IsNullOrWhiteSpace(edit.InvoiceNumber) ? null : edit.InvoiceNumber.Trim();
            target.Currency = string.IsNullOrWhiteSpace(edit.Currency) ? null : edit.Currency.Trim();
            target.TransactionTime = string.IsNullOrWhiteSpace(edit.TransactionTime) ? null : edit.TransactionTime.Trim();
            target.Subtotal = edit.Subtotal;
            target.GstHst = edit.GstHst;
            target.TotalAmount = edit.TotalAmount;
            target.ReceiptDate = edit.ReceiptDate;
        }
    }

    private static bool TryGetMissingRequiredFieldError(IReadOnlyList<ExtractedReceipt> receipts, out string error)
    {
        var missingInvoice = new List<int>();
        var missingStore = new List<int>();
        var missingDate = new List<int>();
        for (var i = 0; i < receipts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(receipts[i].InvoiceNumber))
            {
                missingInvoice.Add(i + 1);
            }

            if (string.IsNullOrWhiteSpace(receipts[i].StoreName))
            {
                missingStore.Add(i + 1);
            }

            if (receipts[i].ReceiptDate is null)
            {
                missingDate.Add(i + 1);
            }
        }

        if (missingInvoice.Count == 0 && missingStore.Count == 0 && missingDate.Count == 0)
        {
            error = string.Empty;
            return false;
        }

        var parts = new List<string>();
        if (missingInvoice.Count > 0)
        {
            parts.Add(missingInvoice.Count == 1
                ? $"row {missingInvoice[0]} is missing InvoiceNumber"
                : $"rows {string.Join(", ", missingInvoice)} are missing InvoiceNumber");
        }

        if (missingStore.Count > 0)
        {
            parts.Add(missingStore.Count == 1
                ? $"row {missingStore[0]} is missing StoreName"
                : $"rows {string.Join(", ", missingStore)} are missing StoreName");
        }

        if (missingDate.Count > 0)
        {
            parts.Add(missingDate.Count == 1
                ? $"row {missingDate[0]} is missing Date"
                : $"rows {string.Join(", ", missingDate)} are missing Date");
        }

        error = $"Cannot export Excel: {string.Join("; ", parts)}. Enter the required values and try again.";
        return true;
    }

    private static List<ExportFieldDiffDto> BuildFieldDifferences(Receipt existing, ExtractedReceipt preview)
    {
        var diffs = new List<ExportFieldDiffDto>();

        void Add(string field, string? dbValue, string? previewValue)
        {
            var left = dbValue?.Trim() ?? string.Empty;
            var right = previewValue?.Trim() ?? string.Empty;
            if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(new ExportFieldDiffDto
                {
                    Field = field,
                    DatabaseValue = string.IsNullOrEmpty(left) ? "(empty)" : left,
                    PreviewValue = string.IsNullOrEmpty(right) ? "(empty)" : right
                });
            }
        }

        Add("InvoiceNumber", existing.InvoiceNumber, preview.InvoiceNumber);
        Add("StoreName", existing.StoreName, preview.StoreName);
        Add("Currency", existing.Currency, preview.Currency);
        Add("TransactionTime", existing.TransactionTime, preview.TransactionTime);
        Add("Subtotal", existing.Subtotal.ToString("0.00"), preview.Subtotal?.ToString("0.00"));
        Add("HST/GST", existing.GstHst.ToString("0.00"), preview.GstHst?.ToString("0.00"));
        Add("TotalAmount", existing.TotalAmount.ToString("0.00"), preview.TotalAmount?.ToString("0.00"));
        Add("Date", existing.ReceiptDate.ToString("yyyy-MM-dd"), preview.ReceiptDate?.ToString("yyyy-MM-dd"));
        return diffs;
    }
}
