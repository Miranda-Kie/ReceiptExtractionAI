using System.Collections.Concurrent;

using System.Security.Cryptography;

using HstReceipts.Core.Entities;

using HstReceipts.Core.Interfaces;

using Microsoft.Extensions.Logging;



namespace HstReceipts.Infrastructure.Auth;



public sealed class LoginVerificationService : ILoginVerificationService

{

    /// <summary>How long a code remains valid for sign-in, and the minimum wait before resend.</summary>

    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);



    /// <summary>How long a verification session can be refreshed after the code expires.</summary>

    private static readonly TimeSpan SessionRetention = TimeSpan.FromMinutes(30);



    private readonly ConcurrentDictionary<string, PendingChallenge> _challenges = new(StringComparer.Ordinal);

    private readonly ILogger<LoginVerificationService> _logger;



    public LoginVerificationService(ILogger<LoginVerificationService> logger)

    {

        _logger = logger;

    }



    public LoginVerificationChallenge CreateChallenge(AppUser user)

    {

        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Email))

        {

            throw new InvalidOperationException("User has no email for verification.");

        }



        PurgeExpired();



        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var sentAt = DateTimeOffset.UtcNow;

        var expires = sentAt.Add(ChallengeLifetime);

        var masked = MaskEmail(user.Email);



        _challenges[token] = new PendingChallenge(

            token,

            user.Id,

            user.Username,

            user.Role,

            user.Email.Trim(),

            masked,

            code,

            sentAt,

            expires);



        _logger.LogInformation(

            "Login verification code for {Username} ({MaskedEmail}): {Code} (expires {Expires:u})",

            user.Username,

            masked,

            code,

            expires);



        return ToChallenge(_challenges[token]);

    }



    public LoginVerificationChallenge? GetChallenge(string verificationToken)

    {

        if (string.IsNullOrWhiteSpace(verificationToken) ||

            !_challenges.TryGetValue(verificationToken.Trim(), out var pending) ||

            IsSessionGone(pending))

        {

            return null;

        }



        return ToChallenge(pending);

    }



    public bool TryConsume(string verificationToken, string code, out AppUserSnapshot? user)

    {

        user = null;

        if (string.IsNullOrWhiteSpace(verificationToken) || string.IsNullOrWhiteSpace(code))

        {

            return false;

        }



        var token = verificationToken.Trim();

        if (!_challenges.TryGetValue(token, out var pending))

        {

            return false;

        }



        if (pending.ExpiresAtUtc < DateTimeOffset.UtcNow)

        {

            if (IsSessionGone(pending))

            {

                _challenges.TryRemove(token, out _);

            }



            return false;

        }



        var normalizedCode = code.Trim();

        if (!string.Equals(pending.Code, normalizedCode, StringComparison.Ordinal))

        {

            return false;

        }



        _challenges.TryRemove(token, out _);

        user = new AppUserSnapshot(pending.UserId, pending.Username, pending.Role);

        return true;

    }



    public LoginVerificationResendResult Resend(string verificationToken)

    {

        if (string.IsNullOrWhiteSpace(verificationToken) ||

            !_challenges.TryGetValue(verificationToken.Trim(), out var pending) ||

            IsSessionGone(pending))

        {

            if (!string.IsNullOrWhiteSpace(verificationToken))

            {

                _challenges.TryRemove(verificationToken.Trim(), out _);

            }



            return LoginVerificationResendResult.Expired();

        }



        var now = DateTimeOffset.UtcNow;

        var canResendAt = pending.SentAtUtc.Add(ChallengeLifetime);

        if (now < canResendAt)

        {

            var retryAfter = (int)Math.Ceiling((canResendAt - now).TotalSeconds);

            return LoginVerificationResendResult.TooSoon(Math.Max(1, retryAfter));

        }



        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        var sentAt = now;

        var expires = sentAt.Add(ChallengeLifetime);

        var updated = pending with { Code = code, SentAtUtc = sentAt, ExpiresAtUtc = expires };

        _challenges[pending.Token] = updated;



        _logger.LogInformation(

            "Resent login verification code for {Username} ({MaskedEmail}): {Code}",

            pending.Username,

            pending.MaskedEmail,

            code);



        return LoginVerificationResendResult.Success(ToChallenge(updated));

    }



    public static string MaskEmail(string email)

    {

        var trimmed = email.Trim();

        var at = trimmed.IndexOf('@');

        if (at <= 0 || at == trimmed.Length - 1)

        {

            return "***";

        }



        var local = trimmed[..at];

        var domain = trimmed[(at + 1)..];

        if (local.Length <= 2)

        {

            return $"{local[0]}***@{domain}";

        }



        if (local.Length <= 4)

        {

            return $"{local[0]}***{local[^1]}@{domain}";

        }



        var middle = new string('*', Math.Min(local.Length - 4, 12));

        return $"{local[..2]}{middle}{local[^2..]}@{domain}";

    }



    private static LoginVerificationChallenge ToChallenge(PendingChallenge pending) =>

        new(

            pending.Token,

            pending.Email,

            pending.MaskedEmail,

            pending.Code,

            pending.ExpiresAtUtc,

            pending.SentAtUtc,

            pending.SentAtUtc.Add(ChallengeLifetime));



    private static bool IsSessionGone(PendingChallenge pending) =>

        pending.SentAtUtc.Add(SessionRetention) < DateTimeOffset.UtcNow;



    private void PurgeExpired()

    {

        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _challenges)

        {

            if (pair.Value.SentAtUtc.Add(SessionRetention) < now)

            {

                _challenges.TryRemove(pair.Key, out _);

            }

        }

    }



    private sealed record PendingChallenge(

        string Token,

        Guid UserId,

        string Username,

        string Role,

        string Email,

        string MaskedEmail,

        string Code,

        DateTimeOffset SentAtUtc,

        DateTimeOffset ExpiresAtUtc);

}


