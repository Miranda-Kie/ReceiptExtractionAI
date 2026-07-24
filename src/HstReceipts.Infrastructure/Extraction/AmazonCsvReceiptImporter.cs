using System.Globalization;
using System.Text;
using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Extraction;

/// <summary>
/// Imports Amazon Business order CSV exports into receipt rows.
/// </summary>
public class AmazonCsvReceiptImporter
{
    public bool CanHandle(string fileName)
        => string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ExtractedReceipt> Import(Stream stream, string sourceFileName)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return
            [
                new ExtractedReceipt
                {
                    ReceiptName = sourceFileName,
                    Success = false,
                    ErrorMessage = "CSV file is empty."
                }
            ];
        }

        var headers = ParseCsvLine(headerLine);
        var index = headers
            .Select((name, i) => (name: name.Trim().Trim('"'), i))
            .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        if (!index.ContainsKey("Order ID") && !index.ContainsKey("Order Date"))
        {
            return
            [
                new ExtractedReceipt
                {
                    ReceiptName = sourceFileName,
                    Success = false,
                    ErrorMessage = "CSV does not look like an Amazon Business order export."
                }
            ];
        }

        var results = new List<ExtractedReceipt>();
        var rowNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cols = ParseCsvLine(line);
            var orderId = Get(cols, index, "Order ID");
            var orderDate = ParseDate(Get(cols, index, "Order Date"))
                ?? ParseDate(Get(cols, index, "Payment Date"))
                ?? ParseDate(Get(cols, index, "Invoice Issue Date"));

            var federalTax = ParseDecimal(Get(cols, index, "Order Federal Tax"))
                ?? ParseDecimal(Get(cols, index, "Item Federal Tax"))
                ?? 0m;
            var provincialTax = ParseDecimal(Get(cols, index, "Order Provincial Tax"))
                ?? ParseDecimal(Get(cols, index, "Item Provincial Tax"))
                ?? 0m;
            var gstHst = federalTax + provincialTax;

            var total = ParseDecimal(Get(cols, index, "Order net total"))
                ?? ParseDecimal(Get(cols, index, "Payment Amount"))
                ?? ParseDecimal(Get(cols, index, "Item Net Total"));

            var brand = Get(cols, index, "Brand");
            var storeName = string.IsNullOrWhiteSpace(brand) || brand.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                ? "Amazon"
                : $"Amazon ({brand})";

            var receiptName = string.IsNullOrWhiteSpace(orderId)
                ? BuildMultiReceiptName(sourceFileName, rowNumber - 1)
                : $"{Path.GetFileNameWithoutExtension(sourceFileName)} [{orderId}]{Path.GetExtension(sourceFileName)}";

            var receipt = new ExtractedReceipt
            {
                ReceiptName = receiptName,
                StoreName = storeName,
                Subtotal = total is not null
                    ? decimal.Round(total.Value - gstHst, 2, MidpointRounding.AwayFromZero)
                    : null,
                GstHst = gstHst,
                TotalAmount = total,
                ReceiptDate = orderDate,
                Success = true
            };

            if (total is null)
            {
                receipt.Warnings.Add("Could not find total amount.");
            }

            if (orderDate is null)
            {
                receipt.Warnings.Add("Could not find receipt date.");
            }

            results.Add(receipt);
        }

        if (results.Count == 0)
        {
            results.Add(new ExtractedReceipt
            {
                ReceiptName = sourceFileName,
                Success = false,
                ErrorMessage = "No order rows found in CSV."
            });
        }

        return results;
    }

    private static string BuildMultiReceiptName(string sourceFileName, int index)
    {
        var fileName = Path.GetFileName(sourceFileName);
        return $"{Path.GetFileNameWithoutExtension(fileName)} [{index}]{Path.GetExtension(fileName)}";
    }

    private static string? Get(IReadOnlyList<string> cols, IReadOnlyDictionary<string, int> index, string header)
    {
        if (!index.TryGetValue(header, out var i) || i < 0 || i >= cols.Count)
        {
            return null;
        }

        var value = cols[i].Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Replace("CAD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("$", string.Empty)
            .Replace(",", string.Empty)
            .Trim();

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string[] formats =
        [
            "yyyy/MM/dd",
            "yyyy-MM-dd",
            "M/d/yyyy",
            "MM/dd/yyyy",
            "d/M/yyyy"
        ];

        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return DateOnly.FromDateTime(exact);
        }

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : null;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}
