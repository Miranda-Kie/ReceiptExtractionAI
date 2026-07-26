using System.Security.Cryptography;
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
    /// <summary>How many previous password hashes to retain for reuse checks.</summary>
    private const int MaxPasswordHistory = 24;

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

            if (!AppRoles.IsKnownDirectoryRole(role))
            {
                _logger.LogWarning(
                    "Skipping seed user '{Username}': Role must be Owner, Admin, or Officer.",
                    username);
                continue;
            }

            var wasDeleted = await _db.DeletedUsernames
                .AsNoTracking()
                .AnyAsync(d => d.Username == username, cancellationToken);
            if (wasDeleted)
            {
                _logger.LogInformation(
                    "Skipping seed user '{Username}' — previously deleted by an admin.",
                    username);
                continue;
            }

            var email = seed.Email?.Trim();
            await EnsureUserAsync(username, password, role, email, cancellationToken);
        }
    }

    public Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        var lower = normalized.ToLowerInvariant();
        return await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Email != null && u.Email.ToLower() == lower,
                cancellationToken);
    }

    public async Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);

    public async Task<(bool Ok, string? Error, AppUser? User)> CreateUserAsync(
        string username,
        string role,
        string email,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedUser = username?.Trim() ?? string.Empty;
        var normalizedEmail = email?.Trim() ?? string.Empty;
        var nextRole = AppRoles.NormalizeDirectoryRole(role);
        if (nextRole is null)
        {
            return (false, "Role must be Owner, Admin, or Officer.", null);
        }

        var requester = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
        if (requester is null || !AppRoles.CanManageUsers(requester.Role))
        {
            return (false, "Not authorized to create users.", null);
        }

        // Owner may create Owner/Admin/Officer. Admin may create Admin/Officer. Officers cannot create users.
        if (AppRoles.IsOwner(nextRole) && !AppRoles.IsOwner(requester.Role))
        {
            return (false, "Only an Owner can create Owner accounts.", null);
        }

        if (normalizedUser.Length is < 2 or > 64)
        {
            return (false, "Username must be 2–64 characters.", null);
        }

        if (normalizedEmail.Length == 0 ||
            normalizedEmail.Length > 256 ||
            normalizedEmail.Count(c => c == '@') != 1 ||
            normalizedEmail.StartsWith('@') ||
            normalizedEmail.EndsWith('@') ||
            normalizedEmail.Contains(' ', StringComparison.Ordinal))
        {
            return (false, "Enter a valid email address.", null);
        }

        var usernameKey = normalizedUser.ToLowerInvariant();
        var usernameTaken = await _db.Users.AnyAsync(
            u => u.Username.ToLower() == usernameKey,
            cancellationToken);
        if (usernameTaken)
        {
            return (false, "That username is already taken.", null);
        }

        var emailKey = normalizedEmail.ToLowerInvariant();
        var emailTaken = await _db.Users.AnyAsync(
            u => u.Email != null && u.Email.ToLower() == emailKey,
            cancellationToken);
        if (emailTaken)
        {
            return (false, "That email is already used by another account.", null);
        }

        // Allow recreating a previously deleted username (including former seed accounts).
        var priorDelete = await _db.DeletedUsernames
            .FirstOrDefaultAsync(d => d.Username == normalizedUser, cancellationToken);
        if (priorDelete is not null)
        {
            _db.DeletedUsernames.Remove(priorDelete);
        }

        // Placeholder hash until the account holder sets a password via emailed link.
        var bootstrapPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        var now = EasternTime.Now;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = normalizedUser,
            Email = normalizedEmail,
            Role = nextRole,
            IsActive = true,
            CreatedAtEst = now,
            ModifiedAtEst = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, bootstrapPassword);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "{Actor} created {Role} user '{Username}' (set-password link required).",
            requester.Role,
            user.Role,
            user.Username);
        return (true, null, user);
    }

    public async Task<(bool Ok, string? Error)> SetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return (false, "Password must be at least 6 characters.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        var previousHashes = await _db.UserPasswordHistories
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Select(h => h.PasswordHash)
            .ToListAsync(cancellationToken);

        if (PasswordMatchesAnyStoredHash(user, newPassword, previousHashes))
        {
            return (false, "New password cannot be the same as a previously used password.");
        }

        var now = EasternTime.Now;
        var historyCount = await _db.UserPasswordHistories.CountAsync(
            h => h.UserId == userId,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            _db.UserPasswordHistories.Add(new UserPasswordHistory
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PasswordHash = user.PasswordHash,
                CreatedAtEst = now
            });
            historyCount++;
        }

        var username = user.Username;
        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.ModifiedAtEst = now;

        if (historyCount > MaxPasswordHistory)
        {
            var removeCount = historyCount - MaxPasswordHistory;
            var oldest = await _db.UserPasswordHistories
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.CreatedAtEst)
                .Take(removeCount)
                .ToListAsync(cancellationToken);
            if (oldest.Count > 0)
            {
                _db.UserPasswordHistories.RemoveRange(oldest);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Re-read from the database and verify the new password actually works.
        _db.ChangeTracker.Clear();
        var saved = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (saved is null ||
            !MatchesHash(saved, saved.PasswordHash, newPassword))
        {
            _logger.LogError(
                "Password hash was not persisted correctly for user '{Username}' ({UserId}).",
                username,
                userId);
            return (false, "Password could not be saved. Try again or request a new reset link.");
        }

        _logger.LogInformation(
            "Password saved to database for user '{Username}' ({UserId}).",
            saved.Username,
            saved.Id);
        return (true, null);
    }

    private bool PasswordMatchesAnyStoredHash(
        AppUser user,
        string candidatePassword,
        IReadOnlyList<string> previousHashes)
    {
        if (MatchesHash(user, user.PasswordHash, candidatePassword))
        {
            return true;
        }

        foreach (var hash in previousHashes)
        {
            if (MatchesHash(user, hash, candidatePassword))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesHash(AppUser user, string? passwordHash, string candidatePassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, passwordHash, candidatePassword);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public async Task<(bool Ok, string? Error)> ValidateEmailChangeAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (false, "Email is required for authentication.");
        }

        if (trimmed.Length > 256 ||
            trimmed.Count(c => c == '@') != 1 ||
            trimmed.StartsWith('@') ||
            trimmed.EndsWith('@') ||
            trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return (false, "Enter a valid email address.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        if (string.Equals(user.Email?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "That is already the email on this account.");
        }

        var taken = await _db.Users.AnyAsync(
            u => u.Id != userId && u.Email != null && u.Email.ToLower() == trimmed.ToLower(),
            cancellationToken);
        if (taken)
        {
            return (false, "That email is already used by another account.");
        }

        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateEmailAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var (ok, error) = await ValidateEmailChangeAsync(userId, email, cancellationToken);
        if (!ok)
        {
            return (false, error);
        }

        var trimmed = email!.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        user.Email = trimmed;
        user.ModifiedAtEst = EasternTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Account holder updated email for '{Username}'.", user.Username);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateStatusAsync(
        Guid userId,
        bool isActive,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (userId == requestingUserId && !isActive)
        {
            return (false, "You cannot deactivate your own account.");
        }

        var requester = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
        if (requester is null || !AppRoles.CanManageUsers(requester.Role))
        {
            return (false, "Not authorized to change status.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        if (user.IsActive == isActive)
        {
            return (true, null);
        }

        var manageError = AuthorizeManageTarget(requester.Role, user.Role, "change status for");
        if (manageError is not null)
        {
            return (false, manageError);
        }

        if (!isActive && AppRoles.IsOwner(user.Role))
        {
            var activeOwners = await CountActiveRoleAsync(AppRoles.Owner, cancellationToken);
            if (activeOwners <= 1)
            {
                return (false, "Cannot deactivate the last active Owner account.");
            }
        }

        user.IsActive = isActive;
        user.ModifiedAtEst = EasternTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "{Actor} set status for '{Username}' to {Status}.",
            requester.Role,
            user.Username,
            user.Status);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateRoleAsync(
        Guid userId,
        string role,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var nextRole = AppRoles.NormalizeDirectoryRole(role);
        if (nextRole is null)
        {
            return (false, "Role must be Owner, Admin, or Officer.");
        }

        var requester = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
        if (requester is null || !AppRoles.CanManageUsers(requester.Role))
        {
            return (false, "Not authorized to change roles.");
        }

        if (AppRoles.IsOwner(nextRole) && !AppRoles.IsOwner(requester.Role))
        {
            return (false, "Only an Owner can assign the Owner role.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        if (string.Equals(user.Role, nextRole, StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }

        if (userId == requestingUserId)
        {
            return (false, "You cannot change your own role.");
        }

        var manageError = AuthorizeManageTarget(requester.Role, user.Role, "change the role of");
        if (manageError is not null)
        {
            return (false, manageError);
        }

        if (AppRoles.IsOwner(user.Role) && !AppRoles.IsOwner(nextRole) && user.IsActive)
        {
            var activeOwners = await CountActiveRoleAsync(AppRoles.Owner, cancellationToken);
            if (activeOwners <= 1)
            {
                return (false, "Cannot demote the last active Owner account.");
            }
        }

        user.Role = nextRole;
        user.ModifiedAtEst = EasternTime.Now;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "{Actor} set role for '{Username}' to {Role}.",
            requester.Role,
            user.Username,
            user.Role);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteUserAsync(
        Guid userId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (userId == requestingUserId)
        {
            return (false, "You cannot delete your own account.");
        }

        var requester = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestingUserId, cancellationToken);
        if (requester is null || !AppRoles.CanManageUsers(requester.Role))
        {
            return (false, "Not authorized to delete users.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return (false, "User not found.");
        }

        var manageError = AuthorizeManageTarget(requester.Role, user.Role, "delete");
        if (manageError is not null)
        {
            return (false, manageError);
        }

        if (AppRoles.IsOwner(user.Role) && user.IsActive)
        {
            var ownerCount = await CountActiveRoleAsync(AppRoles.Owner, cancellationToken);
            if (ownerCount <= 1)
            {
                return (false, "Cannot delete the last active Owner account.");
            }
        }

        var username = user.Username;

        // Clean related auth rows that are not cascaded via FK.
        var resetTickets = await _db.PasswordResetTickets
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);
        if (resetTickets.Count > 0)
        {
            _db.PasswordResetTickets.RemoveRange(resetTickets);
        }

        var emailChallenges = await _db.EmailChangeChallenges
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        if (emailChallenges.Count > 0)
        {
            _db.EmailChangeChallenges.RemoveRange(emailChallenges);
        }

        // Remember deletion so seed config cannot recreate this username on restart.
        var alreadyTombstoned = await _db.DeletedUsernames
            .AnyAsync(d => d.Username == username, cancellationToken);
        if (!alreadyTombstoned)
        {
            _db.DeletedUsernames.Add(new DeletedUsernameEntity
            {
                Username = username,
                DeletedAtEst = EasternTime.Now
            });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);

        var stillExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId, cancellationToken);
        if (stillExists)
        {
            _logger.LogError("Delete reported success but user '{Username}' ({UserId}) still exists.", username, userId);
            return (false, "User could not be removed from the database. Try again.");
        }

        _logger.LogInformation(
            "{Actor} deleted user '{Username}' ({UserId}) from the database.",
            requester.Role,
            username,
            userId);
        return (true, null);
    }

    private Task<int> CountActiveRoleAsync(string role, CancellationToken cancellationToken) =>
        _db.Users.CountAsync(
            u => u.Role == role && u.IsActive,
            cancellationToken);

    /// <summary>
    /// Admins may manage Officers only. Owners may manage Admins, Officers, and other Owners
    /// (last-Owner / self rules are enforced by callers).
    /// </summary>
    private static string? AuthorizeManageTarget(string requesterRole, string targetRole, string action)
    {
        if (AppRoles.IsOwner(targetRole) && !AppRoles.IsOwner(requesterRole))
        {
            return $"Only an Owner can {action} an Owner account.";
        }

        if (AppRoles.IsAdmin(targetRole) && !AppRoles.IsOwner(requesterRole))
        {
            return $"Only an Owner can {action} an Admin account.";
        }

        return null;
    }

    private async Task EnsureUserAsync(
        string username,
        string password,
        string role,
        string? email,
        CancellationToken cancellationToken)
    {
        var normalizedRole = AppRoles.NormalizeDirectoryRole(role) ?? AppRoles.Officer;
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (existing is not null)
        {
            // Only fill a missing email from seed. Never overwrite an admin-confirmed address.
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(email))
            {
                existing.Email = email.Trim();
                existing.ModifiedAtEst = EasternTime.Now;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Set missing email for seed user '{Username}'.", username);
            }

            return;
        }

        var now = EasternTime.Now;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            Role = normalizedRole,
            IsActive = true,
            CreatedAtEst = now,
            ModifiedAtEst = now
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {Role} user '{Username}' with status {Status}.",
            normalizedRole,
            username,
            user.Status);
    }
}
