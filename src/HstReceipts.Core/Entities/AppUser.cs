namespace HstReceipts.Core.Entities;

public static class AppRoles
{
    /// <summary>Full admin powers, and can manage Admin accounts (role/status/delete).</summary>
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Officer = "Officer";
    /// <summary>Cookie-only portfolio demo: OCR/export-only, no database save.</summary>
    public const string Demo = "Demo";

    public static bool IsOwner(string? role) =>
        string.Equals(role, Owner, StringComparison.OrdinalIgnoreCase);

    public static bool IsAdmin(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase);

    public static bool IsOfficer(string? role) =>
        string.Equals(role, Officer, StringComparison.OrdinalIgnoreCase);

    /// <summary>Owner or Admin — can access the Users directory.</summary>
    public static bool CanManageUsers(string? role) => IsOwner(role) || IsAdmin(role);

    public static bool IsKnownDirectoryRole(string? role) =>
        IsOwner(role) || IsAdmin(role) || IsOfficer(role);

    public static string? NormalizeDirectoryRole(string? role)
    {
        if (IsOwner(role)) return Owner;
        if (IsAdmin(role)) return Admin;
        if (IsOfficer(role)) return Officer;
        return null;
    }
}

public class AppUser
{
    public Guid Id { get; set; }

    /// <summary>Unique login name.</summary>
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Unique email used for login verification and password reset.</summary>
    public string? Email { get; set; }

    /// <summary>Owner, Admin, or Officer.</summary>
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
