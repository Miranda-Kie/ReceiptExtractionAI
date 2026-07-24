using System.Globalization;
using System.Text.Json;
using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Learning;

public sealed class OpenAiUsageRecorder : IOpenAiUsageRecorder
{
    private readonly ReceiptDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AiLearningOptions _options;
    private readonly ILogger<OpenAiUsageRecorder> _logger;

    public OpenAiUsageRecorder(
        ReceiptDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IOptions<AiLearningOptions> options,
        ILogger<OpenAiUsageRecorder> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(
        string operation,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        if (_options.MaxCallsPerUserPerDay <= 0 &&
            _options.MaxTokensPerUserPerDay <= 0 &&
            _options.MaxEstimatedCostUsdPerUserPerDay <= 0)
        {
            return true;
        }

        var username = ResolveUsername();
        var dayStart = EasternTime.Now.Date;

        int calls;
        int tokens;
        decimal cost;
        try
        {
            var today = await _db.AiApiUsageLogs
                .AsNoTracking()
                .Where(u => u.Username == username && u.CreatedAtEst >= dayStart)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Calls = g.Count(),
                    Tokens = g.Sum(x => x.TotalTokens),
                    Cost = g.Sum(x => x.EstimatedCostUsd)
                })
                .FirstOrDefaultAsync(cancellationToken);

            calls = today?.Calls ?? 0;
            tokens = today?.Tokens ?? 0;
            cost = today?.Cost ?? 0m;
        }
        catch (Exception ex)
        {
            // If the usage table is unavailable, allow the call (rules still work without AI).
            _logger.LogWarning(ex, "OpenAI rate-limit check failed for {User}; allowing call.", username);
            return true;
        }

        if (_options.MaxCallsPerUserPerDay > 0 && calls >= _options.MaxCallsPerUserPerDay)
        {
            LogLimitHit(username, operation, context, "calls", calls, _options.MaxCallsPerUserPerDay);
            return false;
        }

        if (_options.MaxTokensPerUserPerDay > 0 && tokens >= _options.MaxTokensPerUserPerDay)
        {
            LogLimitHit(username, operation, context, "tokens", tokens, _options.MaxTokensPerUserPerDay);
            return false;
        }

        if (_options.MaxEstimatedCostUsdPerUserPerDay > 0 &&
            cost >= _options.MaxEstimatedCostUsdPerUserPerDay)
        {
            _logger.LogWarning(
                "OpenAI daily cost limit reached: date={Date:yyyy-MM-dd} EST user={User} operation={Operation} " +
                "usedUsd={Used} limitUsd={Limit} context={Context}",
                dayStart,
                username,
                operation,
                cost.ToString("0.########", CultureInfo.InvariantCulture),
                _options.MaxEstimatedCostUsdPerUserPerDay.ToString("0.########", CultureInfo.InvariantCulture),
                context ?? string.Empty);
            return false;
        }

        return true;
    }

    public async Task RecordAsync(
        string operation,
        string model,
        string responseJson,
        bool success,
        int? httpStatusCode,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var (prompt, completion, total) = ParseUsage(responseJson);
        var cost = EstimateCostUsd(prompt, completion, model);
        var username = ResolveUsername();
        var when = EasternTime.Now;

        _logger.LogInformation(
            "OpenAI usage: date={Date:yyyy-MM-dd HH:mm:ss} EST user={User} operation={Operation} model={Model} " +
            "promptTokens={Prompt} completionTokens={Completion} totalTokens={Total} " +
            "estimatedCostUsd={Cost} success={Success} httpStatus={Status} context={Context}",
            when,
            username,
            operation,
            string.IsNullOrWhiteSpace(model) ? _options.Model : model,
            prompt,
            completion,
            total,
            cost.ToString("0.########", CultureInfo.InvariantCulture),
            success,
            httpStatusCode,
            context ?? string.Empty);

        try
        {
            _db.AiApiUsageLogs.Add(new AiApiUsageLog
            {
                Id = Guid.NewGuid(),
                CreatedAtEst = when,
                Username = username,
                Operation = Truncate(operation, 64),
                Model = Truncate(string.IsNullOrWhiteSpace(model) ? _options.Model : model, 64),
                PromptTokens = prompt,
                CompletionTokens = completion,
                TotalTokens = total,
                EstimatedCostUsd = cost,
                Success = success,
                HttpStatusCode = httpStatusCode,
                Context = string.IsNullOrWhiteSpace(context) ? null : Truncate(context, 512)
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never fail the main AI flow because audit persistence failed.
            _logger.LogWarning(ex, "Failed to persist OpenAI usage log for {Operation}.", operation);
        }
    }

    public static (int Prompt, int Completion, int Total) ParseUsage(string? responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return (0, 0, 0);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("usage", out var usage))
            {
                return (0, 0, 0);
            }

            var prompt = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
            var completion = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
            var total = usage.TryGetProperty("total_tokens", out var t)
                ? t.GetInt32()
                : prompt + completion;
            return (prompt, completion, total);
        }
        catch (JsonException)
        {
            return (0, 0, 0);
        }
    }

    private void LogLimitHit(
        string username,
        string operation,
        string? context,
        string dimension,
        decimal used,
        decimal limit)
    {
        _logger.LogWarning(
            "OpenAI daily {Dimension} limit reached: date={Date:yyyy-MM-dd} EST user={User} operation={Operation} " +
            "used={Used} limit={Limit} context={Context}",
            dimension,
            EasternTime.Now.Date,
            username,
            operation,
            used,
            limit,
            context ?? string.Empty);
    }

    private decimal EstimateCostUsd(int promptTokens, int completionTokens, string model)
    {
        var (inputPer1M, outputPer1M) = ResolveRates(model);
        var input = promptTokens / 1_000_000m * inputPer1M;
        var output = completionTokens / 1_000_000m * outputPer1M;
        return decimal.Round(input + output, 8, MidpointRounding.AwayFromZero);
    }

    private (decimal InputPer1M, decimal OutputPer1M) ResolveRates(string model)
    {
        var input = _options.InputUsdPer1MTokens > 0 ? _options.InputUsdPer1MTokens : 0.15m;
        var output = _options.OutputUsdPer1MTokens > 0 ? _options.OutputUsdPer1MTokens : 0.60m;

        var id = (model ?? string.Empty).Trim().ToLowerInvariant();
        if (_options.InputUsdPer1MTokens <= 0 || _options.OutputUsdPer1MTokens <= 0)
        {
            if (id.Contains("gpt-4o-mini", StringComparison.Ordinal))
            {
                input = 0.15m;
                output = 0.60m;
            }
            else if (id.Contains("gpt-4o", StringComparison.Ordinal))
            {
                input = 2.50m;
                output = 10.00m;
            }
        }

        return (input, output);
    }

    private string ResolveUsername()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return _httpContextAccessor.HttpContext is null ? "system" : "anonymous";
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
