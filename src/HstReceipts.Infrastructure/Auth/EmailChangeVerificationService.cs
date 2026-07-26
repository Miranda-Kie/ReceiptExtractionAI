using System.Security.Cryptography;
using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HstReceipts.Infrastructure.Auth;

/// <summary>
/// DB-backed email-change codes so verification survives app restarts.
/// </summary>
public sealed class EmailChangeVerificationService : IEmailChangeVerificationService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionRetention = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailChangeVerificationService> _logger;

    public EmailChangeVerificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailChangeVerificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public EmailChangeChallenge CreateChallenge(Guid userId, string username, string newEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newEmail);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        PurgeExpired(db);

        // Invalidate any unused prior challenges for this user.
        var prior = db.EmailChangeChallenges
            .Where(c => c.UserId == userId && !c.Consumed)
            .ToList();
        foreach (var old in prior)
        {
            old.Consumed = true;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var sentAt = DateTimeOffset.UtcNow;
        var expires = sentAt.Add(ChallengeLifetime);
        var email = newEmail.Trim();
        var masked = LoginVerificationService.MaskEmail(email);

        var entity = new EmailChangeChallengeEntity
        {
            Id = Guid.NewGuid(),
            Token = token,
            UserId = userId,
            Username = username,
            NewEmail = email,
            MaskedEmail = masked,
            Code = code,
            SentAtUtc = sentAt,
            ExpiresAtUtc = expires,
            CreatedAtEst = EasternTime.Now,
            Consumed = false
        };
        db.EmailChangeChallenges.Add(entity);
        db.SaveChanges();

        _logger.LogInformation(
            "Email-change verification code for {Username} → {MaskedEmail}: {Code} (expires {Expires:u})",
            username,
            masked,
            code,
            expires);

        return ToChallenge(entity);
    }

    public EmailChangeChallenge? GetChallenge(string verificationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindSession(db, verificationToken.Trim());
        return pending is null ? null : ToChallenge(pending);
    }

    public EmailChangeResendResult Resend(string verificationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            return EmailChangeResendResult.Expired();
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindSession(db, verificationToken.Trim());
        if (pending is null)
        {
            return EmailChangeResendResult.Expired();
        }

        var now = DateTimeOffset.UtcNow;
        var canResendAt = pending.SentAtUtc.Add(ChallengeLifetime);
        if (now < canResendAt)
        {
            var retryAfter = (int)Math.Ceiling((canResendAt - now).TotalSeconds);
            return EmailChangeResendResult.TooSoon(Math.Max(1, retryAfter));
        }

        pending.Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        pending.SentAtUtc = now;
        pending.ExpiresAtUtc = now.Add(ChallengeLifetime);
        db.SaveChanges();

        _logger.LogInformation(
            "Resent email-change verification code for {Username} → {MaskedEmail}: {Code}",
            pending.Username,
            pending.MaskedEmail,
            pending.Code);

        return EmailChangeResendResult.Success(ToChallenge(pending));
    }

    public bool TryValidate(string verificationToken, string code, Guid expectedUserId, out string? newEmail)
    {
        newEmail = null;
        if (string.IsNullOrWhiteSpace(verificationToken) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindActive(db, verificationToken.Trim());
        if (pending is null || pending.UserId != expectedUserId)
        {
            return false;
        }

        if (!string.Equals(pending.Code, code.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        newEmail = pending.NewEmail;
        return true;
    }

    public void Consume(string verificationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = db.EmailChangeChallenges.FirstOrDefault(c => c.Token == verificationToken.Trim());
        if (pending is null || pending.Consumed)
        {
            return;
        }

        pending.Consumed = true;
        db.SaveChanges();
    }

    private static EmailChangeChallengeEntity? FindActive(ReceiptDbContext db, string token)
    {
        var pending = db.EmailChangeChallenges.FirstOrDefault(c => c.Token == token);
        if (pending is null || pending.Consumed || pending.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return pending;
    }

    private static EmailChangeChallengeEntity? FindSession(ReceiptDbContext db, string token)
    {
        var pending = db.EmailChangeChallenges.FirstOrDefault(c => c.Token == token);
        if (pending is null || pending.Consumed)
        {
            return null;
        }

        if (pending.SentAtUtc.Add(SessionRetention) < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return pending;
    }

    private static void PurgeExpired(ReceiptDbContext db)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var stale = db.EmailChangeChallenges
            .Where(c => c.ExpiresAtUtc < cutoff || (c.Consumed && c.ExpiresAtUtc < DateTimeOffset.UtcNow))
            .ToList();
        if (stale.Count > 0)
        {
            db.EmailChangeChallenges.RemoveRange(stale);
        }
    }

    private static EmailChangeChallenge ToChallenge(EmailChangeChallengeEntity pending) =>
        new(
            pending.Token,
            pending.UserId,
            pending.Username,
            pending.NewEmail,
            pending.MaskedEmail,
            pending.Code,
            pending.ExpiresAtUtc,
            pending.SentAtUtc,
            pending.SentAtUtc.Add(ChallengeLifetime));
}
