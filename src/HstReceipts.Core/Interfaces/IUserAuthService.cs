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
