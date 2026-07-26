using System.Text.Json;
using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HstReceipts.Infrastructure.Repositories;

public sealed class ProcessingBatchRepository : IProcessingBatchRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ReceiptDbContext _db;

    public ProcessingBatchRepository(ReceiptDbContext db)
    {
        _db = db;
    }

    public async Task<ProcessingBatch> CreateAsync(
        Guid batchId,
        string username,
        int totalFiles,
        CancellationToken cancellationToken = default)
    {
        var batch = new ProcessingBatch
        {
            Id = batchId,
            Username = username,
            Status = ProcessingBatchStatuses.Pending,
            TotalFiles = totalFiles,
            CreatedAtEst = EasternTime.Now
        };
        _db.ProcessingBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);
        return batch;
    }

    public async Task AddResultsAsync(
        Guid batchId,
        string sourceFileName,
        IReadOnlyList<ExtractedReceipt> rows,
        bool fileSucceeded,
        CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            var batch = await _db.ProcessingBatches
                .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                ?? throw new InvalidOperationException($"Processing batch {batchId} was not found.");

            batch.Status = ProcessingBatchStatuses.Processing;
            if (fileSucceeded)
            {
                batch.CompletedFiles++;
            }
            else
            {
                batch.FailedFiles++;
            }

            foreach (var row in rows)
            {
                _db.ProcessingBatchResults.Add(new ProcessingBatchResult
                {
                    BatchId = batchId,
                    SourceFileName = sourceFileName,
                    ReceiptName = row.ReceiptName,
                    StoreName = row.StoreName,
                    InvoiceNumber = row.InvoiceNumber,
                    Currency = row.Currency,
                    TransactionTime = row.TransactionTime,
                    Subtotal = row.Subtotal,
                    GstHst = row.GstHst,
                    TotalAmount = row.TotalAmount,
                    ReceiptDate = row.ReceiptDate,
                    SourceTextPreview = row.SourceTextPreview is { Length: > 4000 } t ? t[..4000] : row.SourceTextPreview,
                    Success = row.Success,
                    ErrorMessage = row.ErrorMessage,
                    WarningsJson = JsonSerializer.Serialize(row.Warnings ?? [], JsonOptions),
                    CreatedAtEst = EasternTime.Now
                });
            }

            if (batch.CompletedFiles + batch.FailedFiles >= batch.TotalFiles && batch.TotalFiles > 0)
            {
                batch.Status = batch.CompletedFiles > 0
                    ? ProcessingBatchStatuses.Completed
                    : ProcessingBatchStatuses.Failed;
                batch.CompletedAtEst = EasternTime.Now;
                if (batch.Status == ProcessingBatchStatuses.Failed)
                {
                    batch.ErrorMessage ??= "All files failed Document Intelligence processing.";
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        });
    }

    public async Task<ReceiptPipelineBatchStatus> GetStatusAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _db.ProcessingBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return new ReceiptPipelineBatchStatus
            {
                BatchId = batchId,
                Status = "failed",
                ErrorMessage = "Batch not found."
            };
        }

        var results = await _db.ProcessingBatchResults
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var receipts = results.Select(MapRow).ToList();
        var status = batch.Status.ToLowerInvariant() switch
        {
            "completed" => "completed",
            "failed" => "failed",
            _ => "processing"
        };

        return new ReceiptPipelineBatchStatus
        {
            BatchId = batch.Id,
            Status = status,
            TotalFiles = batch.TotalFiles,
            CompletedFiles = batch.CompletedFiles,
            FailedFiles = batch.FailedFiles,
            ErrorMessage = batch.ErrorMessage,
            Receipts = receipts
        };
    }

    private static ExtractedReceipt MapRow(ProcessingBatchResult row)
    {
        List<string> warnings;
        try
        {
            warnings = JsonSerializer.Deserialize<List<string>>(row.WarningsJson, JsonOptions) ?? [];
        }
        catch
        {
            warnings = [];
        }

        return new ExtractedReceipt
        {
            ReceiptName = row.ReceiptName,
            StoreName = row.StoreName,
            InvoiceNumber = row.InvoiceNumber,
            Currency = row.Currency,
            TransactionTime = row.TransactionTime,
            Subtotal = row.Subtotal,
            GstHst = row.GstHst,
            TotalAmount = row.TotalAmount,
            ReceiptDate = row.ReceiptDate,
            SourceTextPreview = row.SourceTextPreview,
            Success = row.Success,
            ErrorMessage = row.ErrorMessage,
            Warnings = warnings
        };
    }
}
