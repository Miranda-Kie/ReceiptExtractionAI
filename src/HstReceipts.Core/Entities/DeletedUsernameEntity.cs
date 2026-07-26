namespace HstReceipts.Core.Entities;

/// <summary>
/// Usernames intentionally removed by an admin. Prevents seed config from recreating them.
/// </summary>
public class DeletedUsernameEntity
{
    public string Username { get; set; } = string.Empty;
    public DateTime DeletedAtEst { get; set; }
}
