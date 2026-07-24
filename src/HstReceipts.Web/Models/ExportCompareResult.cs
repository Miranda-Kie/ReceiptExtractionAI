namespace HstReceipts.Web.Models;

public sealed class ExportCompareResult
{
    public int NewCount { get; set; }
    public int UnchangedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ExportConflictDto> Conflicts { get; set; } = [];
}

public sealed class ExportConflictDto
{
    public string StoreName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd when a matching DB row is found.</summary>
    public string? ReceiptDate { get; set; }

    /// <summary>Strong (invoice+date) or Soft (store+date+total).</summary>
    public string MatchKind { get; set; } = "Strong";

    /// <summary>
    /// True when a matching DB row exists (user must confirm before overwriting).
    /// </summary>
    public bool SameDateMatch { get; set; }

    public List<ExportFieldDiffDto> Differences { get; set; } = [];
}

public sealed class ExportFieldDiffDto
{
    public string Field { get; set; } = string.Empty;
    public string? DatabaseValue { get; set; }
    public string? PreviewValue { get; set; }
}
