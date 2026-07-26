using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Extraction;

public class ReceiptProcessingService : IReceiptProcessingService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp", ".pdf", ".csv"
    };

    private readonly IEnumerable<ITextExtractor> _extractors;
    private readonly IDocumentReceiptAnalyzer _documentAnalyzer;
    private readonly IReceiptFieldExtractor _fieldExtractor;
    private readonly AmazonCsvReceiptImporter _csvImporter;
    private readonly ReceiptProcessingOptions _options;
    private readonly ILogger<ReceiptProcessingService> _logger;

    public ReceiptProcessingService(
        IEnumerable<ITextExtractor> extractors,
        IDocumentReceiptAnalyzer documentAnalyzer,
        IReceiptFieldExtractor fieldExtractor,
        AmazonCsvReceiptImporter csvImporter,
        IOptions<ReceiptProcessingOptions> options,
        ILogger<ReceiptProcessingService> logger)
    {
        _extractors = extractors;
        _documentAnalyzer = documentAnalyzer;
        _fieldExtractor = fieldExtractor;
        _csvImporter = csvImporter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReceiptBatchResult> ProcessUploadsAsync(
        IEnumerable<UploadedReceiptFile> files,
        bool allowAzureServices = false,
        CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid();
        var fileList = files.ToList();
        var results = new ConcurrentBag<(int Index, IReadOnlyList<ExtractedReceipt> Rows)>();

        var maxParallel = Math.Clamp(
            _options.MaxOcrParallelism > 0 ? _options.MaxOcrParallelism : Environment.ProcessorCount,
            1,
            Math.Min(4, Math.Max(1, fileList.Count)));

        await Parallel.ForEachAsync(
            fileList.Select((file, index) => (file, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallel,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var rows = await ProcessOneAsync(item.file, ct, allowAzureServices);
                results.Add((item.index, rows));
            });

        var ordered = results
            .OrderBy(r => r.Index)
            .SelectMany(r => r.Rows)
            .ToList();

        foreach (var row in ordered)
        {
            ReceiptFieldExtractor.FinalizeAiPremiumFoodMartRow(row);
            ExtractedReceiptValidator.Apply(row);
        }

        return new ReceiptBatchResult
        {
            BatchId = batchId,
            Receipts = ordered
        };
    }

    private async Task<IReadOnlyList<ExtractedReceipt>> ProcessOneAsync(
        UploadedReceiptFile file,
        CancellationToken cancellationToken,
        bool allowAzureServices = false)
    {
        // Folder uploads often include a relative path (e.g. "test1/Shoppers.pdf").
        // Keep it on ReceiptName so the preview/Excel show where the file came from.
        var relativePath = NormalizeUploadPath(file.FileName);
        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return [];
        }

        var receiptLabel = string.IsNullOrWhiteSpace(relativePath) ? fileName : relativePath;

        var ext = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(ext))
        {
            return
            [
                new ExtractedReceipt
                {
                    ReceiptName = receiptLabel,
                    Success = false,
                    ErrorMessage = $"Unsupported file type '{ext}'.",
                    Warnings = { $"Skipped unsupported file: {receiptLabel}" }
                }
            ];
        }

        try
        {
            if (_csvImporter.CanHandle(fileName))
            {
                var csvRows = _csvImporter.Import(file.Content, receiptLabel).ToList();
                foreach (var row in csvRows)
                {
                    if (string.IsNullOrWhiteSpace(row.ReceiptName) ||
                        string.Equals(row.ReceiptName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        row.ReceiptName = receiptLabel;
                    }

                    ExtractedReceiptValidator.Apply(row);
                }

                return csvRows;
            }

            // Each parallel worker needs its own stream position.
            if (file.Content.CanSeek)
            {
                file.Content.Position = 0;
            }

            // Prefer Azure Document Intelligence when configured and the caller is an Owner.
            if (allowAzureServices && _documentAnalyzer.CanHandle(fileName))
            {
                return await _documentAnalyzer.AnalyzeAsync(file.Content, receiptLabel, cancellationToken);
            }

            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(fileName));
            if (extractor is null)
            {
                var missing = new ExtractedReceipt
                {
                    ReceiptName = receiptLabel,
                    Success = false,
                    ErrorMessage = allowAzureServices && _documentAnalyzer.IsAvailable
                        ? "Unsupported file type for Document Intelligence."
                        : "No text extractor available. Sign in as Owner to use Azure Document Intelligence.",
                };
                ExtractedReceiptValidator.Apply(missing);
                return [missing];
            }

            var text = await extractor.ExtractTextAsync(file.Content, fileName, cancellationToken);
            var rows = _fieldExtractor.ExtractAll(text, receiptLabel).ToList();
            var preview = text.Length > 4000 ? text[..4000] : text;
            foreach (var row in rows)
            {
                row.SourceTextPreview ??= preview;
            }

            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process receipt {FileName}", receiptLabel);
            var failed = new ExtractedReceipt
            {
                ReceiptName = receiptLabel,
                Success = false,
                ErrorMessage = ex.Message,
                Warnings = { $"Processing failed: {ex.Message}" }
            };
            ExtractedReceiptValidator.Apply(failed);
            return [failed];
        }
    }

    private static string NormalizeUploadPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim().Trim('"');

        // Keep full local paths from server-side folder scans (e.g. C:\Users\...\Shoppers.pdf).
        if (Path.IsPathRooted(trimmed) || Regex.IsMatch(trimmed, @"^[A-Za-z]:[\\/]"))
        {
            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        var normalized = trimmed.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }
}
