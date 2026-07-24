using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Models;
using HstReceipts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HstReceipts.Infrastructure.Repositories;

public class ReceiptRepository : IReceiptRepository
{
    private readonly ReceiptDbContext _db;

    public ReceiptRepository(ReceiptDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Receipt>> SaveBatchAsync(
        Guid batchId,
        IEnumerable<ExtractedReceipt> receipts,
        CancellationToken cancellationToken = default)
    {
        var entities = receipts
            .Where(r => r.Success)
            .Where(HasRequiredDbFields)
            .Select(r =>
            {
                ExtractedReceiptValidator.Apply(r);
                return ToEntity(batchId, r, ReceiptMatchStatuses.New);
            })
            .ToList();

        _db.Receipts.AddRange(entities);
        await _db.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public async Task<IReadOnlyList<Receipt>> GetByBatchIdAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Receipts
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.CreatedAtEst)
            .ToListAsync(cancellationToken);
    }

    public async Task<(Receipt? Receipt, string MatchKind)> FindMatchAsync(
        string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        var invoice = NormalizeInvoice(invoiceNumber);
        if (string.IsNullOrWhiteSpace(invoice))
        {
            return (null, ReceiptMatchStatuses.New);
        }

        var existing = await _db.Receipts
            .FirstOrDefaultAsync(r => r.InvoiceNumber == invoice, cancellationToken);

        return existing is null
            ? (null, ReceiptMatchStatuses.New)
            : (existing, ReceiptMatchStatuses.Strong);
    }

    public async Task<(int Inserted, int Updated, int Skipped, int Corrections)> UpsertWithCorrectionsAsync(
        Guid batchId,
        IEnumerable<ExtractedReceipt> receipts,
        string username,
        CancellationToken cancellationToken = default)
    {
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var corrections = 0;
        var now = EasternTime.Now;
        var actor = string.IsNullOrWhiteSpace(username) ? "unknown" : username.Trim();

        foreach (var receipt in receipts)
        {
            ExtractedReceiptValidator.Apply(receipt);

            if (!HasRequiredDbFields(receipt))
            {
                skipped++;
                continue;
            }

            var invoice = NormalizeInvoice(receipt.InvoiceNumber);
            var (existing, matchKind) = await FindMatchAsync(invoice, cancellationToken);

            if (existing is null)
            {
                _db.Receipts.Add(ToEntity(batchId, receipt, ReceiptMatchStatuses.New, now));
                inserted++;
                continue;
            }

            corrections += AppendCorrections(existing, receipt, batchId, actor, matchKind, now);

            existing.InvoiceNumber = invoice;
            existing.StoreName = receipt.StoreName!.Trim();
            existing.Subtotal = receipt.Subtotal ?? 0m;
            existing.GstHst = receipt.GstHst ?? 0m;
            existing.TotalAmount = receipt.TotalAmount ?? 0m;
            existing.Currency = receipt.Currency!.Trim();
            existing.ReceiptDate = receipt.ReceiptDate!.Value;
            existing.TransactionTime = receipt.TransactionTime;
            existing.MatchStatus = matchKind;
            existing.BatchId = batchId;
            existing.ModifiedAtEst = now;
            updated++;
        }

        if (inserted > 0 || updated > 0 || corrections > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return (inserted, updated, skipped, corrections);
    }

    private int AppendCorrections(
        Receipt existing,
        ExtractedReceipt preview,
        Guid batchId,
        string username,
        string matchKind,
        DateTime now)
    {
        var count = 0;

        void Track(string field, string? oldValue, string? newValue)
        {
            var left = oldValue?.Trim() ?? string.Empty;
            var right = newValue?.Trim() ?? string.Empty;
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                return;
            }

            _db.ReceiptCorrections.Add(new ReceiptCorrection
            {
                Id = Guid.NewGuid(),
                ReceiptId = existing.Id,
                BatchId = batchId,
                Username = username,
                FieldName = field,
                OldValue = string.IsNullOrEmpty(left) ? null : left,
                NewValue = string.IsNullOrEmpty(right) ? null : right,
                MatchKind = matchKind,
                CreatedAtEst = now
            });
            count++;
        }

        Track("InvoiceNumber", existing.InvoiceNumber, NormalizeInvoice(preview.InvoiceNumber));
        Track("StoreName", existing.StoreName, preview.StoreName);
        Track("Currency", existing.Currency, preview.Currency);
        Track("Subtotal", FormatMoney(existing.Subtotal), FormatMoney(preview.Subtotal));
        Track("GstHst", FormatMoney(existing.GstHst), FormatMoney(preview.GstHst));
        Track("TotalAmount", FormatMoney(existing.TotalAmount), FormatMoney(preview.TotalAmount));
        Track("ReceiptDate", existing.ReceiptDate.ToString("yyyy-MM-dd"), preview.ReceiptDate?.ToString("yyyy-MM-dd"));
        Track("TransactionTime", existing.TransactionTime, preview.TransactionTime);

        return count;
    }

    private static string NormalizeInvoice(string? invoice)
        => invoice?.Trim() ?? string.Empty;

    private static string? FormatMoney(decimal? value)
        => value is null ? null : value.Value.ToString("0.00");

    private static bool HasRequiredDbFields(ExtractedReceipt receipt)
        => !string.IsNullOrWhiteSpace(receipt.InvoiceNumber)
           && !string.IsNullOrWhiteSpace(receipt.StoreName)
           && !string.IsNullOrWhiteSpace(receipt.Currency)
           && receipt.ReceiptDate is not null;

    private static Receipt ToEntity(
        Guid batchId,
        ExtractedReceipt r,
        string matchStatus,
        DateTime? now = null)
    {
        var stamp = now ?? EasternTime.Now;
        return new Receipt
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            InvoiceNumber = NormalizeInvoice(r.InvoiceNumber),
            StoreName = r.StoreName!.Trim(),
            Currency = r.Currency!.Trim(),
            Subtotal = r.Subtotal ?? 0m,
            GstHst = r.GstHst ?? 0m,
            TotalAmount = r.TotalAmount ?? 0m,
            ReceiptDate = r.ReceiptDate!.Value,
            TransactionTime = r.TransactionTime,
            MatchStatus = matchStatus,
            CreatedAtEst = stamp,
            ModifiedAtEst = null
        };
    }
}
