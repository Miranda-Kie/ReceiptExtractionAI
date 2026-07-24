using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace HstReceipts.Infrastructure.Extraction;

public class PdfTextExtractor : ITextExtractor
{
    private const int FastDpi = 180;
    private const int GoodEnoughScore = 4;
    private const int NativeTextMinLength = 40;

    private readonly ReceiptProcessingOptions _options;
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(
        IOptions<ReceiptProcessingOptions> options,
        ILogger<PdfTextExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool CanHandle(string fileName)
        => string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var pdfBytes = buffer.ToArray();

        using var document = PdfDocument.Open(pdfBytes);
        var pageCount = document.NumberOfPages;
        var nativePages = new string[pageCount];
        var needsOcr = new List<int>();
        var pageIndex = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = BuildPageText(page);
            nativePages[pageIndex] = pageText;
            if (string.IsNullOrWhiteSpace(pageText) || pageText.Trim().Length < NativeTextMinLength)
            {
                needsOcr.Add(pageIndex);
            }

            pageIndex++;
        }

        if (needsOcr.Count == 0)
        {
            return JoinPages(nativePages);
        }

        var tessDataPath = ResolveTessDataPath();
        if (!Directory.Exists(tessDataPath) || !File.Exists(Path.Combine(tessDataPath, "eng.traineddata")))
        {
            _logger.LogWarning("Cannot OCR scanned PDF {FileName}: tessdata missing at {Path}", fileName, tessDataPath);
            return JoinPages(nativePages);
        }

        var ocrPages = new ConcurrentDictionary<int, string>();
        var maxParallel = Math.Clamp(
            _options.MaxOcrParallelism > 0 ? _options.MaxOcrParallelism : Environment.ProcessorCount,
            1,
            Math.Min(4, needsOcr.Count));

        _logger.LogInformation(
            "OCR {Count} of {Total} pages in {FileName} (parallelism={Parallelism})",
            needsOcr.Count,
            pageCount,
            fileName,
            maxParallel);

        await Parallel.ForEachAsync(
            needsOcr,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallel,
                CancellationToken = cancellationToken
            },
            async (index, ct) =>
            {
                var text = await OcrConcurrency.RunAsync(
                    () => OcrRenderedPage(pdfBytes, index, fileName, tessDataPath),
                    ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ocrPages[index] = text;
                }
            });

        for (var i = 0; i < pageCount; i++)
        {
            if (ocrPages.TryGetValue(i, out var ocrText))
            {
                nativePages[i] = ocrText;
            }
        }

        return JoinPages(nativePages);
    }

    private static string JoinPages(IReadOnlyList<string> pages)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\f');
            }

            sb.AppendLine(pages[i] ?? string.Empty);
        }

        return sb.ToString();
    }

    private static string BuildPageText(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            return page.Text ?? string.Empty;
        }

        var heights = words
            .Select(w => w.BoundingBox.Height)
            .Where(h => h > 0)
            .OrderBy(h => h)
            .ToList();
        var medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 8.0;
        var yTolerance = Math.Max(2.0, medianHeight * 0.6);

        var ordered = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<List<Word>>();
        foreach (var word in ordered)
        {
            var baseline = word.BoundingBox.Bottom;
            var line = lines.LastOrDefault();
            if (line is null)
            {
                lines.Add([word]);
                continue;
            }

            var lineBaseline = line.Average(w => w.BoundingBox.Bottom);
            if (Math.Abs(lineBaseline - baseline) <= yTolerance)
            {
                line.Add(word);
            }
            else
            {
                lines.Add([word]);
            }
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var text = string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
            }
        }

        return sb.ToString();
    }

    private string OcrRenderedPage(byte[] pdfBytes, int pageIndex, string fileName, string tessDataPath)
    {
        try
        {
            var photoInvoice = IsPhotoInvoiceFile(fileName);
            // Wrinkled thermal photos are often extremely tall; prefer a sharper render and
            // OCR the top band first (store + subtotal/HST/total usually live there).
            var preferSharp =
                fileName.Contains("Canadian Tire", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("CanadianTire", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("AI Food Mart", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Food Mart", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Costco", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("Golden Panda", StringComparison.OrdinalIgnoreCase);
            // Side-by-side cheque+invoice / phone photos of invoices are enormous — stay on FastDpi.
            var forceFastDpi =
                photoInvoice ||
                fileName.Contains("Golden Panda", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("GoldenPanda", StringComparison.OrdinalIgnoreCase);
            var dpi = forceFastDpi ? FastDpi : preferSharp ? 260 : FastDpi;

            using var bitmap = Conversion.ToImage(
                pdfBytes,
                pageIndex,
                null,
                new RenderOptions(Dpi: dpi));

            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly);
            engine.SetVariable("user_defined_dpi", dpi.ToString());

            var (bestText, bestScore) = OcrBitmapVariants(engine, bitmap, dpi, fileName, pageIndex, photoInvoice);

            // Phone photos of paper invoices are often rotated 90/270° (Pest / JS Best).
            // Skip rotation for upright cheque+invoice scans (Dumplings / Golden Panda).
            var tryRotation =
                fileName.Contains("Pest", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("JS Best", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("JSBest", StringComparison.OrdinalIgnoreCase);
            if (photoInvoice && tryRotation && bestScore < 10)
            {
                foreach (var degrees in new[] { 270, 90 })
                {
                    using var rotated = RotateBitmap(bitmap, degrees);
                    var (rotText, rotScore) = OcrBitmapVariants(
                        engine,
                        rotated,
                        dpi,
                        fileName,
                        pageIndex,
                        photoInvoice,
                        rotationLabel: degrees);
                    if (rotScore > bestScore)
                    {
                        bestText = rotText;
                        bestScore = rotScore;
                    }

                    if (bestScore >= 12)
                    {
                        break;
                    }
                }
            }

            // Last resort: full-page OCR at higher DPI when still weak (skip huge side-by-side scans).
            var wideSideBySide = IsWideSideBySideScan(fileName, bitmap.Width, bitmap.Height);
            if (bestScore < GoodEnoughScore && !wideSideBySide && !photoInvoice)
            {
                using var sharp = Conversion.ToImage(
                    pdfBytes,
                    pageIndex,
                    null,
                    new RenderOptions(Dpi: 300));
                engine.SetVariable("user_defined_dpi", "300");
                var full = OcrBitmap(engine, sharp, 300, "bin");
                var fullScore = ScoreReceiptOcrText(full);
                if (fullScore > bestScore)
                {
                    bestText = full;
                    bestScore = fullScore;
                }

                _logger.LogDebug(
                    "OCR full retry {FileName} p{Page} score={Score}",
                    fileName,
                    pageIndex + 1,
                    bestScore);
            }

            return bestText ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rendered OCR failed for {FileName} page {Page}", fileName, pageIndex + 1);
            return string.Empty;
        }
    }

    private (string? Text, int Score) OcrBitmapVariants(
        TesseractEngine engine,
        SKBitmap bitmap,
        int dpi,
        string fileName,
        int pageIndex,
        bool photoInvoice,
        int? rotationLabel = null)
    {
        var tall = bitmap.Height > (int)(bitmap.Width * 2.2);
        var wideSideBySide = IsWideSideBySideScan(fileName, bitmap.Width, bitmap.Height);
        var faintThermal =
            fileName.Contains("Yours Food", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("YoursFood", StringComparison.OrdinalIgnoreCase);
        string? bestText = null;
        var bestScore = int.MinValue;
        var mergedParts = new List<string>();

        // Photo invoices: hardbin on crops; money/totals also try otsu-bin (Dumplings).
        // Faint thermal slips (Yours Food Mart) need hardbin too.
        var prepModes = photoInvoice || faintThermal
            ? new[] { "hardbin", "bin" }
            : tall || wideSideBySide
                ? new[] { "bin", "gray" }
                : new[] { "gray" };

        foreach (var region in GetOcrRegions(bitmap.Width, bitmap.Height, photoInvoice, fileName))
        {
            string? regionBest = null;
            var regionBestScore = int.MinValue;
            using var regionBitmap = CropBitmap(bitmap, region);
            using var scaled = TryDownscaleForOcr(regionBitmap, maxEdge: photoInvoice ? 1600 : 2200);
            var ocrSource = scaled ?? regionBitmap;
            var modesForRegion = prepModes;
            if (photoInvoice && region.Name is "top" or "mid-right" or "money" or "left-voucher" or "footer")
            {
                modesForRegion = ["hardbin", "bin"];
            }

            foreach (var prep in modesForRegion)
            {
                var text = OcrBitmap(engine, ocrSource, dpi, prep);
                var score = ScoreReceiptOcrText(text);
                _logger.LogDebug(
                    "OCR {FileName} p{Page} dpi={Dpi} rot={Rot} region={Region} prep={Prep} score={Score}",
                    fileName,
                    pageIndex + 1,
                    dpi,
                    rotationLabel?.ToString() ?? "0",
                    region.Name,
                    prep,
                    score);

                if (score > regionBestScore)
                {
                    regionBestScore = score;
                    regionBest = text;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestText = text;
                }

                if (!tall && !wideSideBySide && !photoInvoice && score >= 12)
                {
                    break;
                }
            }

            if ((tall || wideSideBySide || photoInvoice) &&
                regionBestScore >= 2 &&
                !string.IsNullOrWhiteSpace(regionBest))
            {
                mergedParts.Add(regionBest!);
            }

            if (!tall && !wideSideBySide && !photoInvoice && bestScore >= 12)
            {
                break;
            }
        }

        if (mergedParts.Count > 1)
        {
            bestText = string.Join("\n\n", mergedParts);
            bestScore = Math.Max(bestScore, ScoreReceiptOcrText(bestText));
        }

        return (bestText, bestScore == int.MinValue ? 0 : bestScore);
    }

    private static bool IsPhotoInvoiceFile(string fileName)
        => fileName.Contains("JS Best", StringComparison.OrdinalIgnoreCase) ||
           fileName.Contains("JSBest", StringComparison.OrdinalIgnoreCase) ||
           fileName.Contains("Pest", StringComparison.OrdinalIgnoreCase) ||
           fileName.Contains("Dumpling", StringComparison.OrdinalIgnoreCase) ||
           fileName.Contains("FoodsUp", StringComparison.OrdinalIgnoreCase) ||
           fileName.Contains("Foods Up", StringComparison.OrdinalIgnoreCase);

    // Only true cheque+invoice pair scans (Golden Panda). Portrait phone photos of FoodsUp
    // invoices also match the old width/height heuristic and were OCR'd on the wrong crops.
    private static bool IsWideSideBySideScan(string fileName, int width, int height)
        => (fileName.Contains("Golden Panda", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("GoldenPanda", StringComparison.OrdinalIgnoreCase)) &&
           width >= 1400 &&
           width >= (int)(height * 0.55);

    private static List<(string Name, int X, int Y, int Width, int Height)> GetOcrRegions(
        int width,
        int height,
        bool photoInvoice = false,
        string? fileName = null)
    {
        var regions = new List<(string Name, int X, int Y, int Width, int Height)>();
        fileName ??= string.Empty;

        // Flat phone photos of paper invoices (JS Best / Pest / Dumplings / FoodsUp).
        // Skip full-page OCR — these phone shots are multi-megapixel and hang Tesseract.
        if (photoInvoice)
        {
            var isFoodsUp = fileName.Contains("FoodsUp", StringComparison.OrdinalIgnoreCase) ||
                            fileName.Contains("Foods Up", StringComparison.OrdinalIgnoreCase);
            if (isFoodsUp)
            {
                // App invoice: date/header at top, CAD TOTAL + TAX in the lower half.
                var topH = Math.Max(400, (int)(height * 0.40));
                var footY = (int)(height * 0.35);
                regions.Add(("top", 0, 0, width, topH));
                regions.Add(("footer", 0, footY, width, height - footY));
                return regions;
            }

            var topH2 = Math.Max(350, (int)(height * 0.32));
            var moneyX = (int)(width * 0.45);
            var moneyY = (int)(height * 0.45);
            var leftW = Math.Max(400, (int)(width * 0.42));
            var leftH = Math.Min(Math.Max(700, (int)(height * 0.45)), 2200);
            regions.Add(("top", 0, 0, width, topH2));
            // Handwritten cheque / amount stub (Dumplings).
            regions.Add(("left-voucher", 0, 0, leftW, leftH));
            regions.Add(("money", moneyX, moneyY, width - moneyX, height - moneyY));
            // After 270° rotation the totals often sit mid-right on a landscape page.
            if (width > height)
            {
                var midX = (int)(width * 0.45);
                var midY = (int)(height * 0.20);
                var midH = Math.Max(400, (int)(height * 0.70));
                regions.Add(("mid-right", midX, midY, width - midX, Math.Min(midH, height - midY)));
            }

            return regions;
        }

        // Cheque stub + invoice side-by-side (Golden Panda only).
        if (IsWideSideBySideScan(fileName, width, height))
        {
            var half = width / 2;
            // Cheque BALANCE sits mid-stub; keep width modest but cover ~40% height.
            var leftH = Math.Min(Math.Max(700, (int)(height * 0.40)), 2000);
            regions.Add(("left-voucher", 0, 0, half, leftH));
            regions.Add(("right-header", half, 0, width - half, Math.Min(800, height)));
            // Invoice SUBTOTAL / HST / BALANCE DUE usually live bottom-right.
            var footH = Math.Max(500, (int)(height * 0.35));
            var footY = Math.Max(0, height - footH);
            regions.Add(("right-footer", half, footY, width - half, height - footY));
        }

        // Tall phone-photo receipts: header at top, money summary mid-page, card/date at bottom.
        if (height > (int)(width * 2.2))
        {
            var topH = Math.Max(400, (int)(height * 0.38));
            var totalsY = (int)(height * 0.18);
            var totalsH = Math.Max(500, (int)(height * 0.38));
            var midY = (int)(height * 0.40);
            var midH = Math.Max(400, (int)(height * 0.25));
            var footerH = Math.Max(450, (int)(height * 0.28));
            var footerY = Math.Max(0, height - footerH);
            regions.Add(("totals", 0, totalsY, width, Math.Min(totalsH, height - totalsY)));
            regions.Add(("top", 0, 0, width, topH));
            regions.Add(("mid", 0, midY, width, Math.Min(midH, height - midY)));
            regions.Add(("footer", 0, footerY, width, height - footerY));
            return regions;
        }

        if (regions.Count == 0)
        {
            regions.Add(("full", 0, 0, width, height));
        }

        return regions;
    }

    private static SKBitmap RotateBitmap(SKBitmap source, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        var swap = degrees is 90 or 270;
        var rotated = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.White);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }

    private static SKBitmap? TryDownscaleForOcr(SKBitmap source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge)
        {
            return null;
        }

        var scale = maxEdge / (float)longest;
        var w = Math.Max(1, (int)(source.Width * scale));
        var h = Math.Max(1, (int)(source.Height * scale));
        var scaled = new SKBitmap(w, h);
        using var canvas = new SKCanvas(scaled);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, SKRect.Create(0, 0, w, h));
        return scaled;
    }

    private static SKBitmap CropBitmap(SKBitmap source, (string Name, int X, int Y, int Width, int Height) region)
    {
        var w = Math.Min(region.Width, source.Width - region.X);
        var h = Math.Min(region.Height, source.Height - region.Y);
        var cropped = new SKBitmap(w, h);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(
            source,
            SKRect.Create(region.X, region.Y, w, h),
            SKRect.Create(0, 0, w, h));
        return cropped;
    }

    private static string OcrBitmap(TesseractEngine engine, SKBitmap bitmap, int dpi, string prep)
    {
        string? tempPath = null;
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            tempPath = Path.Combine(Path.GetTempPath(), $"hst-ocr-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(tempPath, encoded.ToArray());

            using var pix = Pix.LoadFromFile(tempPath);
            using var gray = pix.ConvertRGBToGray();
            Pix working = gray;
            Pix? scaled = null;
            Pix? binary = null;
            try
            {
                if (working.Width < 1600 || working.Height < 1600)
                {
                    scaled = working.Scale(1.5f, 1.5f);
                    working = scaled;
                }

                if (string.Equals(prep, "hardbin", StringComparison.OrdinalIgnoreCase))
                {
                    // Fixed threshold helps shadowed phone photos of paper invoices.
                    binary = working.BinarizeOtsuAdaptiveThreshold(16, 16, 0, 0, 0.15f);
                    working = binary;
                }
                else if (string.Equals(prep, "bin", StringComparison.OrdinalIgnoreCase))
                {
                    binary = working.BinarizeOtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f);
                    working = binary;
                }

                using var page = engine.Process(working, PageSegMode.SparseText);
                return page.GetText() ?? string.Empty;
            }
            finally
            {
                binary?.Dispose();
                scaled?.Dispose();
            }
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static int ScoreReceiptOcrText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var score = 0;
        if (Regex.IsMatch(text, @"\bSUB[-\s]*TOTAL\b", RegexOptions.IgnoreCase))
        {
            score += 4;
        }

        if (Regex.IsMatch(text, @"\bPAID\b|\bINVOICE\s+DATE\b", RegexOptions.IgnoreCase))
        {
            score += 3;
        }

        if (Regex.IsMatch(text, @"\bTOTAL\b|\bTOT\s*AY\b|\bM/?C\s*TEND\b", RegexOptions.IgnoreCase))
        {
            score += 4;
        }

        if (Regex.IsMatch(text, @"\b\d{1,2}\s*%\s*HST\b|\bHST\b|\bGST\b|\bTAX\b", RegexOptions.IgnoreCase))
        {
            score += 3;
        }

        if (Regex.IsMatch(text, @"\bCredit\s*Card\b|\bTotal\s+after\s+tax\b|\bAMOUNT\s*:\s*\$?", RegexOptions.IgnoreCase))
        {
            score += 4;
        }

        if (Regex.IsMatch(text, @"\bBALANCE\b|\bTHIS\s+CHEQUE\b|\bPick\s+Up\s+Cash\b", RegexOptions.IgnoreCase))
        {
            score += 3;
        }

        if (Regex.IsMatch(text, @"\bCOSTCO\b|\bMaster\s*Card\b|\bAPPROVED\b", RegexOptions.IgnoreCase))
        {
            score += 3;
        }

        if (Regex.IsMatch(text, @"\b20\d{2}[/-]\d{2}[/-]\d{2}\b"))
        {
            score += 2;
        }

        score += Math.Min(8, Regex.Matches(text, @"\b\d{1,4}\.\d{2}\b").Count);
        return score;
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
