using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfilePermission> ProfilePermissions => Set<ProfilePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

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

        model.Entity<Supplier>(ConfigureContact);
        model.Entity<Customer>(ConfigureContact);

        model.Entity<Article>(e =>
        {
            e.Property(a => a.Reference).HasMaxLength(50);
            e.Property(a => a.Name).HasMaxLength(200);
            e.Property(a => a.Brand).HasMaxLength(100);
            e.Property(a => a.Category).HasMaxLength(100);
            e.Property(a => a.Compatibility).HasMaxLength(500);
            e.Property(a => a.Description).HasMaxLength(1000);
            e.Property(a => a.Location).HasMaxLength(50);
            e.Property(a => a.PurchasePrice).HasPrecision(18, 3);
            e.Property(a => a.SalePrice).HasPrecision(18, 3);
            e.HasIndex(a => a.Reference).IsUnique();
            e.HasIndex(a => a.Name);
            e.HasOne(a => a.Supplier).WithMany(s => s.Articles).HasForeignKey(a => a.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Order>(e =>
        {
            e.Property(o => o.Notes).HasMaxLength(1000);
            e.Property(o => o.VatRate).HasPrecision(5, 4);
            e.Property(o => o.Subtotal).HasPrecision(18, 3);
            e.Property(o => o.VatAmount).HasPrecision(18, 3);
            e.Property(o => o.Total).HasPrecision(18, 3);
            e.Property(o => o.AmountPaid).HasPrecision(18, 3);
            e.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(o => o.OrderDate);
            e.HasIndex(o => o.PaymentStatus);
            e.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<OrderLine>(e =>
        {
            e.Property(l => l.Reference).HasMaxLength(50);
            e.Property(l => l.Name).HasMaxLength(200);
            e.Property(l => l.UnitPrice).HasPrecision(18, 3);
            e.HasOne<Order>().WithMany(o => o.Lines).HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Article>().WithMany().HasForeignKey(l => l.ArticleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureContact<T>(EntityTypeBuilder<T> e) where T : BusinessContact
    {
        e.Property(c => c.Name).HasMaxLength(200);
        e.Property(c => c.ContactName).HasMaxLength(200);
        e.Property(c => c.Email).HasMaxLength(256);
        e.Property(c => c.Phone).HasMaxLength(30);
        e.Property(c => c.Address).HasMaxLength(300);
        e.Property(c => c.City).HasMaxLength(100);
        e.Property(c => c.TaxNumber).HasMaxLength(50);
        e.Property(c => c.Notes).HasMaxLength(1000);
        e.HasIndex(c => c.Name).IsUnique();
    }
}
