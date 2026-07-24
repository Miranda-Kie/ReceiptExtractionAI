using HstReceipts.Core.Models;

namespace HstReceipts.Core.Interfaces;

public interface IExcelExportService
{
    byte[] Export(IEnumerable<ExtractedReceipt> receipts, ExcelExportColumns? columns = null);
}
