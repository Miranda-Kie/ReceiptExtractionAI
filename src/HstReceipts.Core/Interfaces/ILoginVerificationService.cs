using HstReceipts.Core.Entities;



namespace HstReceipts.Core.Interfaces;



public interface ILoginVerificationService

{

    LoginVerificationChallenge CreateChallenge(AppUser user);



    LoginVerificationChallenge? GetChallenge(string verificationToken);



    bool TryConsume(string verificationToken, string code, out AppUserSnapshot? user);



    LoginVerificationResendResult Resend(string verificationToken);

}



public sealed record LoginVerificationChallenge(

    string VerificationToken,

    string Email,

    string MaskedEmail,

    string Code,

    DateTimeOffset ExpiresAtUtc,

    DateTimeOffset SentAtUtc,

    DateTimeOffset CanResendAtUtc);



public sealed record LoginVerificationResendResult(

    bool Ok,

    LoginVerificationChallenge? Challenge,

    string? Error,

    int? RetryAfterSeconds)

{

    public static LoginVerificationResendResult Success(LoginVerificationChallenge challenge) =>

        new(true, challenge, null, null);



    public static LoginVerificationResendResult TooSoon(int retryAfterSeconds) =>

        new(

            false,

            null,

            $"A verification code was already sent. You can request a new one in {FormatWait(retryAfterSeconds)}.",

            retryAfterSeconds);



    public static LoginVerificationResendResult Expired() =>

        new(false, null, "Verification session expired. Sign in again.", null);



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



public sealed record AppUserSnapshot(Guid Id, string Username, string Role);


