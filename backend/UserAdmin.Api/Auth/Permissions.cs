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
    public const string ArticlesRead = "articles.read";
    public const string ArticlesWrite = "articles.write";
    public const string SuppliersRead = "suppliers.read";
    public const string SuppliersWrite = "suppliers.write";
    public const string CustomersRead = "customers.read";
    public const string CustomersWrite = "customers.write";
    public const string OrdersRead = "orders.read";
    public const string OrdersWrite = "orders.write";

    public static readonly IReadOnlyList<PermissionInfo> Catalog =
    [
        new(UsersRead, "Users", "View users"),
        new(UsersWrite, "Users", "Create, edit and delete users"),
        new(ProfilesRead, "Profiles", "View profiles"),
        new(ProfilesWrite, "Profiles", "Create, edit and delete profiles"),
        new(ArticlesRead, "Articles", "View articles and stock"),
        new(ArticlesWrite, "Articles", "Create, edit and delete articles"),
        new(SuppliersRead, "Suppliers", "View suppliers"),
        new(SuppliersWrite, "Suppliers", "Create, edit and delete suppliers"),
        new(CustomersRead, "Customers", "View customers"),
        new(CustomersWrite, "Customers", "Create, edit and delete customers"),
        new(OrdersRead, "Orders", "View and print orders"),
        new(OrdersWrite, "Orders", "Create, edit and delete orders and record payments"),
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
