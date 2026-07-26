namespace HstReceipts.Core.Options;

/// <summary>
/// Initial login accounts created on startup when missing.
/// Passwords must come from local/secret config — never commit real passwords.
/// </summary>
public class SeedUsersOptions
{
    public const string SectionName = "SeedUsers";

    public List<SeedUserOptions> Users { get; set; } = [];
}

public class SeedUserOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>Optional email for post-login verification codes.</summary>
    public string? Email { get; set; }
}
