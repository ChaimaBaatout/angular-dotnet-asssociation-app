namespace UserAdmin.Api.Domain;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = "";

    /// <summary>Changing this invalidates every access token already issued to the user.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>An access profile: a named set of permissions assigned to users.</summary>
public class Profile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>System profiles always hold every permission and cannot be edited or deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>The profile given to people who sign up themselves. Cannot be deleted.</summary>
    public bool IsDefault { get; set; }

    public List<ProfilePermission> Permissions { get; set; } = [];
    public List<User> Users { get; set; } = [];
}

public class ProfilePermission
{
    public int ProfileId { get; set; }
    public required string Permission { get; set; }
}

public enum UserTokenPurpose
{
    /// <summary>Sent by email to choose a password, either after signing up or after forgetting it.</summary>
    SetPassword,

    /// <summary>The secret inside an account's sign-in QR code.</summary>
    QrLogin,
}

/// <summary>A secret that acts on behalf of a user. Only its hash is stored; each user has at most one per purpose.</summary>
public class UserToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public UserTokenPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public User User { get; set; } = null!;
}

public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>All tokens descended from one login share a family, so a replayed token can revoke them together.</summary>
    public Guid FamilyId { get; set; }

    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
