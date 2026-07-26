namespace HstReceipts.Core.Interfaces;

/// <summary>
/// Plaintext passwords known for Admin Users "Show" (seed baseline, create, or reset).
/// SQL still stores only password hashes; this store is for admin UI reveal only.
/// </summary>
public interface IAdminCredentialStore
{
    void Remember(string username, string password);

    /// <summary>
    /// Seed config baseline for Show. Does not overwrite a password learned from create/reset.
    /// </summary>
    void RememberSeedBaseline(string username, string password);

    void Forget(string username);

    string? TryGet(string username);
}
