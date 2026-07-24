namespace HstReceipts.Core.Entities;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Officer = "Officer";
    /// <summary>Cookie-only portfolio demo: OCR/export-only, no database save.</summary>
    public const string Demo = "Demo";
}

public class AppUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Admin or Officer.</summary>
    public string Role { get; set; } = AppRoles.Officer;

    /// <summary>When false, the account cannot sign in (status Inactive).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Account created time in Eastern Time (server-local EST/EDT).</summary>
    public DateTime CreatedAtEst { get; set; }

    /// <summary>Last modified time in Eastern Time (server-local EST/EDT).</summary>
    public DateTime ModifiedAtEst { get; set; }

    /// <summary>Display status derived from <see cref="IsActive"/>.</summary>
    public string Status => IsActive ? UserStatus.Active : UserStatus.Inactive;
}

public static class UserStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
}
