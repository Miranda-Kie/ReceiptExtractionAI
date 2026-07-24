namespace HstReceipts.Core.Interfaces;

/// <summary>
/// Records OpenAI chat/completions token usage, estimated cost, user, and timestamp.
/// Also enforces per-user daily rate limits before new API calls.
/// </summary>
public interface IOpenAiUsageRecorder
{
    /// <summary>
    /// Returns true if the current user is under today's call/token/cost caps.
    /// Logs and returns false when a limit is exceeded (caller should skip the API call).
    /// </summary>
    Task<bool> TryAcquireAsync(
        string operation,
        string? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses <c>usage</c> from the API JSON body, estimates cost, writes an app log line,
    /// and persists an <c>AiApiUsageLogs</c> row.
    /// </summary>
    Task RecordAsync(
        string operation,
        string model,
        string responseJson,
        bool success,
        int? httpStatusCode,
        string? context = null,
        CancellationToken cancellationToken = default);
}
