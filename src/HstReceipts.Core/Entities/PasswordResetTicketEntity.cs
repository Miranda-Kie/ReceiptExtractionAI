namespace HstReceipts.Core.Entities;

/// <summary>
/// One-time password reset ticket (emailed link and/or verification code).
/// </summary>
public class PasswordResetTicketEntity
{
    public Guid Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string MaskedEmail { get; set; } = string.Empty;

    /// <summary>Six-digit verification code for forgot-password on the sign-in page.</summary>
    public string Code { get; set; } = string.Empty;

    public DateTimeOffset SentAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTime CreatedAtEst { get; set; }

    public bool Consumed { get; set; }

    /// <summary>True when this ticket is a new-account set-password invite (not a reset).</summary>
    public bool IsSetPasswordInvite { get; set; }
}
