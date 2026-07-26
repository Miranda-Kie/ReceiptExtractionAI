namespace HstReceipts.Core.Options;

public class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    /// <summary>
    /// When true and Endpoint + ApiKey are set, Azure Document Intelligence replaces local OCR
    /// for image/PDF receipts.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Document Intelligence resource endpoint, e.g. https://{name}.cognitiveservices.azure.com/</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Resource key (prefer user secrets / local appsettings — never commit).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id. Default is the prebuilt receipt model.</summary>
    public string ModelId { get; set; } = "prebuilt-receipt";

    /// <summary>Optional locale hint (e.g. en-CA).</summary>
    public string? Locale { get; set; } = "en-CA";

    /// <summary>
    /// When true, Document Intelligence OCR text is also run through store-specific rules.
    /// Rules win when they find more receipts (multi-page PDFs) or when DI money fields fail checks.
    /// </summary>
    public bool FillGapsWithRules { get; set; } = true;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
