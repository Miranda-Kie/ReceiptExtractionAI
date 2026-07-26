namespace HstReceipts.Core.Interfaces;

public interface IPasswordResetService
{
    /// <param name="shortLivedCode">
    /// When true, ticket is for sign-in forgot-password codes (10 minutes).
    /// When false, ticket is for emailed links (2 hours).
    /// </param>
    /// <param name="forSetPasswordInvite">
    /// When true, the link is for a new account to set a password (not a reset).
    /// </param>
    PasswordResetTicket CreateTicket(
        Guid userId,
        string username,
        string email,
        bool shortLivedCode = false,
        bool forSetPasswordInvite = false);

    PasswordResetTicket? Peek(string token);

    bool TryConsume(string token, out Guid userId);

    /// <summary>Validates the emailed code without consuming the ticket.</summary>
    bool TryValidateCode(string token, string code, out PasswordResetTicket? ticket);

    PasswordResetResendResult ResendCode(string token);
}

public sealed record PasswordResetTicket(
    string Token,
    Guid UserId,
    string Username,
    string Email,
    string MaskedEmail,
    DateTimeOffset ExpiresAtUtc,
    string ResetPath,
    string Code,
    DateTimeOffset SentAtUtc,
    bool IsSetPasswordInvite = false)
{
    public DateTimeOffset CanResendAtUtc => SentAtUtc.Add(TimeSpan.FromMinutes(10));
}

public sealed record PasswordResetResendResult(
    bool Ok,
    PasswordResetTicket? Ticket,
    string? Error,
    int? RetryAfterSeconds)
{
    public static PasswordResetResendResult Success(PasswordResetTicket ticket) =>
        new(true, ticket, null, null);

    public static PasswordResetResendResult TooSoon(int retryAfterSeconds) =>
        new(false, null, "Please wait before requesting another code.", retryAfterSeconds);

    public static PasswordResetResendResult Expired() =>
        new(false, null, "Verification session expired. Request a new password reset.", null);
}
