using HstReceipts.Core.Models;

namespace HstReceipts.Web.Models;

public class ReceiptPreviewViewModel
{
    public Guid BatchId { get; set; }
    public List<ExtractedReceipt> Receipts { get; set; } = [];
    public string? Message { get; set; }
    public bool SavedToDatabase { get; set; }
    public string? LocalFolderPath { get; set; }
    public ExcelExportColumns ExportColumns { get; set; } = ExcelExportColumns.All();

    /// <summary>Admin-only: show AI learning toggle after login.</summary>
    public bool ShowAiLearningToggle { get; set; }

    /// <summary>True when AiLearning:ApiKey (and BaseUrl/Model) are configured.</summary>
    public bool AiLearningConfigured { get; set; }

    /// <summary>Admin session preference: use AI API on Export Excel.</summary>
    public bool AiLearningEnabled { get; set; }
}
