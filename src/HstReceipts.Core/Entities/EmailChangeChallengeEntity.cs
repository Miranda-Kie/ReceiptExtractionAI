namespace HstReceipts.Core.Entities;

/// <summary>
/// Pending account-holder email-change verification (code emailed to the new address).
/// </summary>
public class EmailChangeChallengeEntity
{
    public Guid Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NewEmail { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset SentAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTime CreatedAtEst { get; set; }
    public bool Consumed { get; set; }
}
