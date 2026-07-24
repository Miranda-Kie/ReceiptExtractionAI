namespace HstReceipts.Core.Entities;

/// <summary>
/// Audit row for each OpenAI (or compatible) chat/completions call.
/// </summary>
public class AiApiUsageLog
{
    public Guid Id { get; set; }

    /// <summary>Eastern time when the API response was recorded.</summary>
    public DateTime CreatedAtEst { get; set; }

    /// <summary>Signed-in username, or "system" / "anonymous".</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>e.g. field_fill, correction_learning</summary>
    public string Operation { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    /// <summary>Estimated USD cost from configured per-1M rates (not billed invoice).</summary>
    public decimal EstimatedCostUsd { get; set; }

    public bool Success { get; set; }

    public int? HttpStatusCode { get; set; }

    /// <summary>Optional context (receipt file name, similarity key, …).</summary>
    public string? Context { get; set; }
}
