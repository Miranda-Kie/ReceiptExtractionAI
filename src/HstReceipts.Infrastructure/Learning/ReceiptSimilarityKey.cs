using System.Text.RegularExpressions;

namespace HstReceipts.Infrastructure.Learning;

internal static class ReceiptSimilarityKey
{
    public static string Build(string receiptName)
    {
        var file = Path.GetFileNameWithoutExtension(receiptName);
        if (string.IsNullOrWhiteSpace(file))
        {
            return string.Empty;
        }

        file = Regex.Replace(file, @"\s*\[\d+\]\s*$", string.Empty);
        file = file.Replace('_', ' ').Replace('-', ' ');
        file = Regex.Replace(file, @"\s+", " ").Trim().ToLowerInvariant();
        return file;
    }
}
