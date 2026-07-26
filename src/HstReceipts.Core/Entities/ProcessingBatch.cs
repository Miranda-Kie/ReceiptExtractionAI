namespace HstReceipts.Core.Entities;

public static class ProcessingBatchStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public class ProcessingBatch
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = ProcessingBatchStatuses.Pending;
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtEst { get; set; }
    public DateTime? CompletedAtEst { get; set; }

    public List<ProcessingBatchResult> Results { get; set; } = [];
}

public class ProcessingBatchResult
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string ReceiptName { get; set; } = string.Empty;
    public string? StoreName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Currency { get; set; }
    public string? TransactionTime { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? GstHst { get; set; }
    public decimal? TotalAmount { get; set; }
    public DateOnly? ReceiptDate { get; set; }
    public string? SourceTextPreview { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string WarningsJson { get; set; } = "[]";
    public DateTime CreatedAtEst { get; set; }

    public ProcessingBatch? Batch { get; set; }
}
