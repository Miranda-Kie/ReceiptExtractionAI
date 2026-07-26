namespace HstReceipts.Core.Options;

public class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public bool Enabled { get; set; }

    /// <summary>Azure Storage connection string (or Azurite for local).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Uploads land here for the Function to pick up.</summary>
    public string InboxContainer { get; set; } = "receipts-inbox";

    /// <summary>Function writes per-file JSON results here.</summary>
    public string ResultsContainer { get; set; } = "receipts-results";

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(ConnectionString);
}

public class ProcessingPipelineOptions
{
    public const string SectionName = "Processing";

    /// <summary>
    /// Inline = API calls Document Intelligence/OCR directly (legacy/local).
    /// Pipeline = React → API → Blob → Azure Function → Document Intelligence → blob preview
    /// (Receipts SQL only on Export Excel and save to database).
    /// </summary>
    public string Mode { get; set; } = "Pipeline";

    public bool UsePipeline =>
        string.Equals(Mode, "Pipeline", StringComparison.OrdinalIgnoreCase);
}
