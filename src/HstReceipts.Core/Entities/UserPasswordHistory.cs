namespace HstReceipts.Core.Entities;



/// <summary>

/// Previously used password hashes for an account (to block reuse).

/// </summary>

public class UserPasswordHistory

{

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtEst { get; set; }



    public AppUser? User { get; set; }

}


