namespace HstReceipts.Core.Models;

public class UploadedReceiptFile
{
    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = Stream.Null;
}
