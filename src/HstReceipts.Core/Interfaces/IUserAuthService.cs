using HstReceipts.Core.Entities;

namespace HstReceipts.Core.Interfaces;

public interface IUserAuthService
{
    /// <summary>
    /// Validates username/password. Returns null when credentials are wrong or the account is inactive.
    /// </summary>
    Task<AppUser?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="ValidateCredentialsAsync"/> but distinguishes inactive accounts from bad passwords.
    /// </summary>
    Task<CredentialValidationResult> ValidateCredentialsDetailedAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task EnsureSeedUsersAsync(CancellationToken cancellationToken = default);

    Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppUser>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user with username/email/role. A random unusable password is stored until
    /// the account holder sets one via the emailed set-password link.
    /// </summary>
    Task<(bool Ok, string? Error, AppUser? User)> CreateUserAsync(
        string username,
        string role,
        string email,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> SetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a proposed email change without saving it.
    /// </summary>
    Task<(bool Ok, string? Error)> ValidateEmailChangeAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> UpdateEmailAsync(
        Guid userId,
        string? email,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> UpdateStatusAsync(
        Guid userId,
        bool isActive,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> UpdateRoleAsync(
        Guid userId,
        string role,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);

    Task<(bool Ok, string? Error)> DeleteUserAsync(
        Guid userId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);
}

public sealed class CredentialValidationResult
{
    public AppUser? User { get; init; }
    public bool Succeeded => User is not null;
    public bool IsInactive { get; init; }
    public string? ErrorMessage { get; init; }

    public static CredentialValidationResult Success(AppUser user) => new() { User = user };

    public static CredentialValidationResult InvalidCredentials() => new()
    {
        ErrorMessage = "Invalid username or password."
    };

    public static CredentialValidationResult Inactive() => new()
    {
        IsInactive = true,
        ErrorMessage = "This account is inactive. Contact an administrator."
    };
}
