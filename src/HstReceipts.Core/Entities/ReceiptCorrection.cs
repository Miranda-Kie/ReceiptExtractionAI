namespace HstReceipts.Core.Entities;

/// <summary>
/// Field-level audit when Export Excel and save overwrites a DB receipt.
/// Avoids silent human/OCR edits — every change is queryable in SQL Server.
/// </summary>
public class ReceiptCorrection
{
    public Guid Id { get; set; }

    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public Guid BatchId { get; set; }

    /// <summary>Username who saved (Admin/Officer). Demo cannot save.</summary>
    public string Username { get; set; } = string.Empty;

    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

  /// <summary>Strong — how the preview was matched to this receipt (InvoiceNumber).</summary>
  public string MatchKind { get; set; } = ReceiptMatchStatuses.Strong;

    public DateTime CreatedAtEst { get; set; }
}
