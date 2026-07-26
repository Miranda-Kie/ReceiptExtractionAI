using System.Security.Cryptography;
using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HstReceipts.Infrastructure.Auth;

/// <summary>
/// DB-backed password reset tickets so emailed links/codes survive app restarts.
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(IServiceScopeFactory scopeFactory, ILogger<PasswordResetService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public PasswordResetTicket CreateTicket(
        Guid userId,
        string username,
        string email,
        bool shortLivedCode = false,
        bool forSetPasswordInvite = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

        PurgeExpired(db);

        var now = DateTimeOffset.UtcNow;
        var lifetime = shortLivedCode ? CodeLifetime : LinkLifetime;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var expires = now.Add(lifetime);
        var masked = LoginVerificationService.MaskEmail(email);

        var prior = db.PasswordResetTickets
            .Where(t => t.UserId == userId && !t.Consumed)
            .ToList();
        foreach (var old in prior)
        {
            old.Consumed = true;
        }

        db.PasswordResetTickets.Add(new PasswordResetTicketEntity
        {
            Id = Guid.NewGuid(),
            Token = token,
            UserId = userId,
            Username = username,
            Email = email.Trim(),
            MaskedEmail = masked,
            Code = code,
            SentAtUtc = now,
            ExpiresAtUtc = expires,
            CreatedAtEst = EasternTime.Now,
            Consumed = false,
            IsSetPasswordInvite = forSetPasswordInvite
        });
        db.SaveChanges();

        var ticket = ToTicket(token, userId, username, email.Trim(), masked, expires, code, now, forSetPasswordInvite);
        _logger.LogInformation(
            "Password {Kind} for {Username} ({MaskedEmail}): path={Path} (expires {Expires:u})",
            forSetPasswordInvite ? "set-invite" : "reset",
            username,
            masked,
            ticket.ResetPath,
            expires);

        return ticket;
    }

    public PasswordResetTicket? Peek(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindActive(db, token.Trim());
        return pending is null ? null : ToTicket(pending);
    }

    public bool TryValidateCode(string token, string code, out PasswordResetTicket? ticket)
    {
        ticket = null;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindActive(db, token.Trim());
        if (pending is null)
        {
            return false;
        }

        if (!string.Equals(pending.Code, code.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        ticket = ToTicket(pending);
        return true;
    }

    public PasswordResetResendResult ResendCode(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return PasswordResetResendResult.Expired();
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindActive(db, token.Trim());
        if (pending is null)
        {
            return PasswordResetResendResult.Expired();
        }

        var now = DateTimeOffset.UtcNow;
        var canResendAt = pending.SentAtUtc.Add(CodeLifetime);
        if (now < canResendAt)
        {
            var retryAfter = (int)Math.Ceiling((canResendAt - now).TotalSeconds);
            return PasswordResetResendResult.TooSoon(Math.Max(1, retryAfter));
        }

        pending.Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        pending.SentAtUtc = now;
        pending.ExpiresAtUtc = now.Add(CodeLifetime);
        db.SaveChanges();

        var ticket = ToTicket(pending);
        _logger.LogInformation(
            "Password-reset code resent for {Username} ({MaskedEmail}): {Code}",
            ticket.Username,
            ticket.MaskedEmail,
            ticket.Code);
        return PasswordResetResendResult.Success(ticket);
    }

    public bool TryConsume(string token, out Guid userId)
    {
        userId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var pending = FindActive(db, token.Trim());
        if (pending is null)
        {
            return false;
        }

        pending.Consumed = true;
        db.SaveChanges();
        userId = pending.UserId;
        return true;
    }

    private static PasswordResetTicketEntity? FindActive(ReceiptDbContext db, string token)
    {
        var pending = db.PasswordResetTickets.FirstOrDefault(t => t.Token == token);
        if (pending is null || pending.Consumed || pending.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return null;
        }

        return pending;
    }

    private static void PurgeExpired(ReceiptDbContext db)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var stale = db.PasswordResetTickets
            .Where(t => t.ExpiresAtUtc < cutoff || (t.Consumed && t.ExpiresAtUtc < DateTimeOffset.UtcNow))
            .ToList();
        if (stale.Count > 0)
        {
            db.PasswordResetTickets.RemoveRange(stale);
        }
    }

    private static PasswordResetTicket ToTicket(PasswordResetTicketEntity pending) =>
        ToTicket(
            pending.Token,
            pending.UserId,
            pending.Username,
            pending.Email,
            pending.MaskedEmail,
            pending.ExpiresAtUtc,
            pending.Code,
            pending.SentAtUtc,
            pending.IsSetPasswordInvite);

    private static PasswordResetTicket ToTicket(
        string token,
        Guid userId,
        string username,
        string email,
        string masked,
        DateTimeOffset expires,
        string code,
        DateTimeOffset sentAt,
        bool forSetPasswordInvite = false)
    {
        var path = forSetPasswordInvite
            ? $"/client/set-password?token={token}"
            : $"/client/reset-password?token={token}";
        return new(
            token,
            userId,
            username,
            email,
            masked,
            expires,
            path,
            code,
            sentAt,
            forSetPasswordInvite);
    }
}
