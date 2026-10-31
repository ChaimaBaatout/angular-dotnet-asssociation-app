using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Data;

public static class DbSeeder
{
    public const string AdministratorProfile = "Administrator";
    private const string StandardProfile = "Standard user";

    public static async Task MigrateAndSeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var admin = await db.Profiles.Include(p => p.Permissions).SingleOrDefaultAsync(p => p.IsSystem);
        if (admin is null)
        {
            admin = new Profile
            {
                Name = AdministratorProfile,
                Description = "Full access to everything. Always includes new permissions.",
                IsSystem = true,
            };
            db.Profiles.Add(admin);
        }

        if (!await db.Profiles.AnyAsync(p => p.IsDefault))
        {
            var standard = await db.Profiles.SingleOrDefaultAsync(p => p.Name == StandardProfile);
            if (standard is null)
            {
                standard = new Profile { Name = StandardProfile, Description = "Can sign in and manage their own account." };
                db.Profiles.Add(standard);
            }
            standard.IsDefault = true;
        }

        admin.Permissions.RemoveAll(p => !Permissions.All.Contains(p.Permission));
        var held = admin.Permissions.Select(p => p.Permission).ToHashSet();
        admin.Permissions.AddRange(Permissions.All.Where(p => !held.Contains(p))
            .Select(p => new ProfilePermission { Permission = p }));
        await db.SaveChangesAsync();

        if (await db.Users.AnyAsync()) return;

        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var email = config["Seed:AdminEmail"] ?? "admin@example.com";
        var configuredPassword = config["Seed:AdminPassword"];
        var password = string.IsNullOrEmpty(configuredPassword) ? Passwords.GenerateTemporary() : configuredPassword;

        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            FirstName = "System",
            LastName = "Administrator",
            ProfileId = admin.Id,
            MustChangePassword = true,
        };
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().Set(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Written to the console rather than the logger so it does not end up in log storage.
        Console.WriteLine();
        Console.WriteLine("=== First administrator created. You will be asked to change this password. ===");
        Console.WriteLine($"    Email:    {user.Email}");
        if (string.IsNullOrEmpty(configuredPassword)) Console.WriteLine($"    Password: {password}");
        Console.WriteLine();
    }
}
