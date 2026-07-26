namespace HstReceipts.Core.Models;

public sealed class PipelineManifest
{
    public Guid BatchId { get; set; }
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public string Status { get; set; } = "processing";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}
