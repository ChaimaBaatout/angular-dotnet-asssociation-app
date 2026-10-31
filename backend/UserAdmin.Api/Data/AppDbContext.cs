using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfilePermission> ProfilePermissions => Set<ProfilePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PhoneNumber).HasMaxLength(30);
            e.Property(u => u.PasswordHash).HasMaxLength(512);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasOne(u => u.Profile).WithMany(p => p.Users).HasForeignKey(u => u.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Profile>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.Description).HasMaxLength(500);
            e.HasIndex(p => p.Name).IsUnique();
        });

        model.Entity<ProfilePermission>(e =>
        {
            e.HasKey(p => new { p.ProfileId, p.Permission });
            e.Property(p => p.Permission).HasMaxLength(100);
            e.HasOne<Profile>().WithMany(p => p.Permissions).HasForeignKey(p => p.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<UserToken>(e =>
        {
            e.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(32);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.UserId, t.Purpose }).IsUnique();
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RefreshToken>(e =>
        {
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.FamilyId);
            e.HasOne<User>().WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
