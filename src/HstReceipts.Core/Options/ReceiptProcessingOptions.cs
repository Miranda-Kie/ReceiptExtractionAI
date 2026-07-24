namespace HstReceipts.Core.Options;

public class ReceiptProcessingOptions
{
    public const string SectionName = "ReceiptProcessing";

    public string TessDataPath { get; set; } = "tessdata";
    public long MaxUploadBytes { get; set; } = 104_857_600;

    /// <summary>
    /// Max concurrent OCR page/file workers. 0 = auto (up to 4).
    /// </summary>
    public int MaxOcrParallelism { get; set; }
}
