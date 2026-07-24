namespace HstReceipts.Core.Entities;

/// <summary>How a saved receipt was identified against existing SQL rows.</summary>
public static class ReceiptMatchStatuses
{
    /// <summary>Matched on InvoiceNumber (unique business key).</summary>
    public const string Strong = "Strong";

    /// <summary>Inserted as a new row (no prior invoice match).</summary>
    public const string New = "New";
}

public class Receipt
{
    public Guid Id { get; set; }

    // Field order matches the upload preview table (left → right).
    /// <summary>Required business key — all DB upserts match on this alone.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;

    public string Currency { get; set; } = "CAD";
    public decimal Subtotal { get; set; }
    public decimal GstHst { get; set; }
    public decimal TotalAmount { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public string? TransactionTime { get; set; }

    /// <summary><see cref="ReceiptMatchStatuses"/> value set on last upsert.</summary>
    public string MatchStatus { get; set; } = ReceiptMatchStatuses.New;

    public Guid BatchId { get; set; }

    /// <summary>Row created time in Eastern Time (server-local EST/EDT).</summary>
    public DateTime CreatedAtEst { get; set; }

    /// <summary>Last modified time in Eastern Time (server-local EST/EDT). Null until first update.</summary>
    public DateTime? ModifiedAtEst { get; set; }

    public ICollection<ReceiptCorrection> Corrections { get; set; } = new List<ReceiptCorrection>();
}
