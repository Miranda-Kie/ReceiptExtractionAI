using ClosedXML.Excel;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;

namespace HstReceipts.Infrastructure.Export;

public class ExcelExportService : IExcelExportService
{
    public byte[] Export(IEnumerable<ExtractedReceipt> receipts, ExcelExportColumns? columns = null)
    {
        columns ??= ExcelExportColumns.All();
        if (!columns.HasAnyColumn)
        {
            columns = ExcelExportColumns.All();
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Receipts");

        var col = 1;
        var headers = new List<(string Title, Action<IXLCell, ExtractedReceipt> Write)>();

        if (columns.InvoiceNumber)
        {
            headers.Add(("Invoice Number", (cell, r) => WriteText(cell, r.InvoiceNumber)));
        }

        if (columns.StoreName)
        {
            headers.Add(("Store Name", (cell, r) => WriteText(cell, r.StoreName)));
        }

        if (columns.Currency)
        {
            headers.Add(("Currency", (cell, r) => WriteText(cell, r.Currency)));
        }

        if (columns.Subtotal)
        {
            headers.Add(("Subtotal", (cell, r) =>
            {
                if (r.Subtotal is not null)
                {
                    cell.Value = r.Subtotal.Value;
                    cell.Style.NumberFormat.Format = "0.00";
                }
            }));
        }

        if (columns.GstHst)
        {
            headers.Add(("GST/HST", (cell, r) =>
            {
                if (r.GstHst is not null)
                {
                    cell.Value = r.GstHst.Value;
                    cell.Style.NumberFormat.Format = "0.00";
                }
            }));
        }

        if (columns.TotalAmount)
        {
            headers.Add(("Total Amount", (cell, r) =>
            {
                if (r.TotalAmount is not null)
                {
                    cell.Value = r.TotalAmount.Value;
                    cell.Style.NumberFormat.Format = "0.00";
                }
            }));
        }

        if (columns.ReceiptDate)
        {
            headers.Add(("Receipt Date", (cell, r) =>
            {
                if (r.ReceiptDate is not null)
                {
                    cell.Value = r.ReceiptDate.Value.ToDateTime(TimeOnly.MinValue);
                    cell.Style.DateFormat.Format = "yyyy-mm-dd";
                }
            }));
        }

        if (columns.TransactionTime)
        {
            headers.Add(("Transaction Time", (cell, r) => WriteText(cell, r.TransactionTime)));
        }

        foreach (var header in headers)
        {
            worksheet.Cell(1, col).Value = header.Title;
            col++;
        }

        if (headers.Count > 0)
        {
            worksheet.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var receipt in receipts)
        {
            ExtractedReceiptValidator.Apply(receipt);
            col = 1;
            foreach (var header in headers)
            {
                header.Write(worksheet.Cell(row, col), receipt);
                col++;
            }

            row++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteText(IXLCell cell, string? value)
    {
        cell.Value = value ?? string.Empty;
        cell.Style.NumberFormat.Format = "@";
    }
}
