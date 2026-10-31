using System.Security.Claims;

namespace UserAdmin.Api.Auth;

public record PermissionInfo(string Code, string Group, string Label);

/// <summary>The permission catalog. Add new feature permissions here; they become policies and appear in the profile editor.</summary>
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string ProfilesRead = "profiles.read";
    public const string ProfilesWrite = "profiles.write";

    public static readonly IReadOnlyList<PermissionInfo> Catalog =
    [
        new(UsersRead, "Users", "View users"),
        new(UsersWrite, "Users", "Create, edit and delete users"),
        new(ProfilesRead, "Profiles", "View profiles"),
        new(ProfilesWrite, "Profiles", "Create, edit and delete profiles"),
    ];

    public static readonly IReadOnlySet<string> All = Catalog.Select(p => p.Code).ToHashSet();
}

public static class AppClaims
{
    public const string Subject = "sub";
    public const string Stamp = "stamp";
    public const string Permission = "perm";
    public const string MustChangePassword = "must_change_password";

    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(Subject)!);

    public static IReadOnlySet<string> GetPermissions(this ClaimsPrincipal principal) =>
        principal.FindAll(Permission).Select(c => c.Value).ToHashSet();
}
