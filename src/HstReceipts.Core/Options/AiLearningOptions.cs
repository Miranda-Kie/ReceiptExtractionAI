namespace HstReceipts.Core.Options;

public class AiLearningOptions
{
    public const string SectionName = "AiLearning";

    /// <summary>
    /// When true, AI learning is available if an ApiKey is configured.
    /// Admins also control a per-session toggle in the UI; this flag is only a default/fallback.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>OpenAI / Azure OpenAI / compatible API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API base URL, e.g. https://api.openai.com/v1</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Chat model id, e.g. gpt-4o-mini</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Max OCR characters sent per receipt to the model.</summary>
    public int MaxSourceChars { get; set; } = 3500;

    /// <summary>
    /// After rule extraction + learned profiles, call the LLM to fill missing/weak fields
    /// from OCR text (structured JSON + local validation). Requires ApiKey.
    /// </summary>
    public bool FillMissingFields { get; set; } = true;

    /// <summary>Max receipts enriched by the LLM per upload batch (cost control).</summary>
    public int MaxFillPerBatch { get; set; } = 20;

    /// <summary>
    /// Estimated USD price per 1M input (prompt) tokens for cost logging.
    /// Defaults match gpt-4o-mini list pricing; override when you change Model.
    /// </summary>
    public decimal InputUsdPer1MTokens { get; set; } = 0.15m;

    /// <summary>
    /// Estimated USD price per 1M output (completion) tokens for cost logging.
    /// </summary>
    public decimal OutputUsdPer1MTokens { get; set; } = 0.60m;

    /// <summary>
    /// Max OpenAI API calls per signed-in user per Eastern calendar day.
    /// 0 = unlimited.
    /// </summary>
    public int MaxCallsPerUserPerDay { get; set; } = 50;

    /// <summary>
    /// Max total tokens (prompt + completion) per user per Eastern day.
    /// 0 = unlimited.
    /// </summary>
    public int MaxTokensPerUserPerDay { get; set; } = 200_000;

    /// <summary>
    /// Max estimated USD spend per user per Eastern day.
    /// 0 = unlimited.
    /// </summary>
    public decimal MaxEstimatedCostUsdPerUserPerDay { get; set; } = 1.00m;
}
