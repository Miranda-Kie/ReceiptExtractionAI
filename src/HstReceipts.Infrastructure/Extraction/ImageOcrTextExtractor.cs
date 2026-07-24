using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tesseract;

namespace HstReceipts.Infrastructure.Extraction;

public class ImageOcrTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".tif", ".tiff", ".bmp"
    };

    private readonly ReceiptProcessingOptions _options;
    private readonly ILogger<ImageOcrTextExtractor> _logger;

    public ImageOcrTextExtractor(
        IOptions<ReceiptProcessingOptions> options,
        ILogger<ImageOcrTextExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool CanHandle(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(ext);
    }

    public async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tessDataPath = ResolveTessDataPath();
        if (!Directory.Exists(tessDataPath))
        {
            throw new InvalidOperationException(
                $"Tesseract tessdata folder not found at '{tessDataPath}'. See README for setup instructions.");
        }

        var engTrainedData = Path.Combine(tessDataPath, "eng.traineddata");
        if (!File.Exists(engTrainedData))
        {
            throw new InvalidOperationException(
                $"Missing eng.traineddata in '{tessDataPath}'. Download it from the tessdata repository.");
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        return await OcrConcurrency.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly);
            engine.SetVariable("user_defined_dpi", "200");

            using var raw = Pix.LoadFromMemory(bytes);
            using var prepared = PrepareForOcr(raw);
            using var page = engine.Process(prepared, PageSegMode.Auto);
            var text = page.GetText() ?? string.Empty;
            _logger.LogDebug(
                "OCR completed for {FileName} ({Confidence:F1}% confidence)",
                fileName,
                page.GetMeanConfidence());
            return text;
        }, cancellationToken);
    }

    private static Pix PrepareForOcr(Pix source)
    {
        // Modest upscale only for small photos — large scans are already slow enough.
        if (source.Width >= 1400 && source.Height >= 1400)
        {
            return source.Clone();
        }

        var scale = source.Width < 900 || source.Height < 900 ? 2.0f : 1.5f;
        return source.Scale(scale, scale);
    }

    private string ResolveTessDataPath()
    {
        if (Path.IsPathRooted(_options.TessDataPath))
        {
            return _options.TessDataPath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.TessDataPath));
    }
}
