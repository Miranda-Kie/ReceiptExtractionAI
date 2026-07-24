using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Extraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: ValidateReceipts <pdf-or-image-or-ocr.txt> [ocr-out.txt]");
    return 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

string text;
var fileName = Path.GetFileName(path);

if (string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
{
    text = await File.ReadAllTextAsync(path);
    if (args.Length > 1)
    {
        fileName = args[1];
    }
}
else
{
    var tessData = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "src", "HstReceipts.Web", "tessdata"));
    if (!Directory.Exists(tessData))
    {
        tessData = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), "src", "HstReceipts.Web", "tessdata"));
    }

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.Configure<ReceiptProcessingOptions>(o =>
    {
        o.MaxOcrParallelism = 2;
        o.TessDataPath = tessData;
    });
    services.AddSingleton<ITextExtractor, PdfTextExtractor>();
    services.AddSingleton<ITextExtractor, ImageOcrTextExtractor>();

    var sp = services.BuildServiceProvider();
    var extractor = sp.GetServices<ITextExtractor>().First(e => e.CanHandle(fileName));
    await using var fs = File.OpenRead(path);
    text = await extractor.ExtractTextAsync(fs, fileName);
    if (args.Length > 1)
    {
        await File.WriteAllTextAsync(args[1], text);
        Console.WriteLine($"OCR written to {args[1]} ({text.Length} chars)");
    }
}

foreach (var r in new ReceiptFieldExtractor().ExtractAll(text, fileName))
{
    Console.WriteLine(
        $"store={r.StoreName} inv={r.InvoiceNumber} cur={r.Currency} time={r.TransactionTime} gst={r.GstHst} total={r.TotalAmount} date={r.ReceiptDate:yyyy-MM-dd} name={r.ReceiptName} ok={r.Success} warn={string.Join(';', r.Warnings)}");
}

return 0;
