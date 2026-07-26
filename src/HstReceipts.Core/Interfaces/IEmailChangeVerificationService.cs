namespace HstReceipts.Core.Interfaces;

public interface IEmailChangeVerificationService
{
    EmailChangeChallenge CreateChallenge(Guid userId, string username, string newEmail);

    EmailChangeChallenge? GetChallenge(string verificationToken);

    EmailChangeResendResult Resend(string verificationToken);

    /// <summary>
    /// Validates token + code without consuming, so a failed DB update can retry.
    /// </summary>
    bool TryValidate(string verificationToken, string code, Guid expectedUserId, out string? newEmail);

    void Consume(string verificationToken);
}

public sealed record EmailChangeChallenge(
    string VerificationToken,
    Guid UserId,
    string Username,
    string NewEmail,
    string MaskedEmail,
    string Code,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset SentAtUtc,
    DateTimeOffset CanResendAtUtc);

public sealed record EmailChangeResendResult(
    bool Ok,
    EmailChangeChallenge? Challenge,
    string? Error,
    int? RetryAfterSeconds)
{
    public static EmailChangeResendResult Success(EmailChangeChallenge challenge) =>
        new(true, challenge, null, null);

    public static EmailChangeResendResult TooSoon(int retryAfterSeconds) =>
        new(
            false,
            null,
            $"A verification code was already sent. You can request a new one in {FormatWait(retryAfterSeconds)}.",
            retryAfterSeconds);

    public static EmailChangeResendResult Expired() =>
        new(false, null, "Verification session expired. Start the email change again.", null);

    private static string FormatWait(int totalSeconds)
    {
        if (totalSeconds <= 60)
        {
            return $"{Math.Max(1, totalSeconds)} second{(totalSeconds == 1 ? "" : "s")}";
        }

        var minutes = (int)Math.Ceiling(totalSeconds / 60.0);
        return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
    }
}
