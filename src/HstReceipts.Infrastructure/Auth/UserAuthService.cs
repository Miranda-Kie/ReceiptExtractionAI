using HstReceipts.Core;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HstReceipts.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly ReceiptDbContext _db;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly SeedUsersOptions _seedUsers;
    private readonly ILogger<UserAuthService> _logger;

    public UserAuthService(
        ReceiptDbContext db,
        IPasswordHasher<AppUser> passwordHasher,
        IOptions<SeedUsersOptions> seedUsers,
        ILogger<UserAuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _seedUsers = seedUsers.Value;
        _logger = logger;
    }

    public async Task<AppUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidateCredentialsDetailedAsync(username, password, cancellationToken);
        return result.User;
    }

    public async Task<CredentialValidationResult> ValidateCredentialsDetailedAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return CredentialValidationResult.InvalidCredentials();
        }

        // Trim both ends so " admin " / "admin " still match the stored username.
        var normalized = username.Trim();
        if (normalized.Length == 0)
        {
            return CredentialValidationResult.InvalidCredentials();
        }

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == normalized, cancellationToken);

        if (user is null)
        {
            return CredentialValidationResult.InvalidCredentials();
        }

        if (!user.IsActive)
        {
            _logger.LogInformation("Sign-in blocked for inactive user {Username}.", user.Username);
            return CredentialValidationResult.Inactive();
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed
            ? CredentialValidationResult.InvalidCredentials()
            : CredentialValidationResult.Success(user);
    }

    public async Task EnsureSeedUsersAsync(CancellationToken cancellationToken = default)
    {
        if (_seedUsers.Users.Count == 0)
        {
            _logger.LogInformation(
                "No SeedUsers configured. Add accounts in appsettings.Development.local.json (gitignored).");
            return;
        }

        foreach (var seed in _seedUsers.Users)
        {
            var username = seed.Username?.Trim() ?? string.Empty;
            var password = seed.Password ?? string.Empty;
            var role = seed.Role?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(role))
            {
                _logger.LogWarning(
                    "Skipping seed user '{Username}': Username, Password, and Role are all required in config.",
                    username);
                continue;
            }

            if (!string.Equals(role, AppRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, AppRoles.Officer, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Skipping seed user '{Username}': Role must be Admin or Officer.",
                    username);
                continue;
            }

            await EnsureUserAsync(username, password, role, cancellationToken);
        }
    }

    private async Task EnsureUserAsync(
        string username,
        string password,
        string role,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Users.AnyAsync(u => u.Username == username, cancellationToken);
        if (exists)
        {
            return;
        }

        var now = EasternTime.Now;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            Role = role,
            IsActive = true,
            CreatedAtEst = now,
            ModifiedAtEst = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {Role} user '{Username}' with status {Status}.",
            role,
            username,
            user.Status);
    }
}
