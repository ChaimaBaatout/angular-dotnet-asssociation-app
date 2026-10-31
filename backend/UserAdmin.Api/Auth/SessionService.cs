using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Auth;

public record JwtSettings(SymmetricSecurityKey Key, string Issuer, string Audience);

/// <summary>Issues short-lived access tokens and rotating refresh tokens (kept in an HttpOnly cookie).</summary>
public class SessionService(
    AppDbContext db,
    JwtSettings jwt,
    IHttpContextAccessor http,
    IWebHostEnvironment env)
{
    public const string RefreshCookie = "refresh_token";
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    /// <summary>Lets two browser tabs refresh at the same moment without looking like a token replay.</summary>
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);

    public async Task<SessionResponse> StartAsync(User user, Guid? familyId = null)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now).ExecuteDeleteAsync();

        var refreshToken = RandomNumberGenerator.GetHexString(64);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId ?? Guid.NewGuid(),
            TokenHash = Hash(refreshToken),
            ExpiresAt = now + RefreshTokenLifetime,
        });
        await db.SaveChangesAsync();

        SetCookie(refreshToken, now + RefreshTokenLifetime);
        return new SessionResponse(CreateAccessToken(user), await GetCurrentUserAsync(user.Id));
    }

    /// <summary>Exchanges the refresh cookie for a new session, or returns null if it is not valid.</summary>
    public async Task<SessionResponse?> RefreshAsync()
    {
        var presented = http.HttpContext!.Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(presented)) return null;

        var hash = Hash(presented);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash);
        var now = DateTimeOffset.UtcNow;
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now) return null;

        if (token.RotatedAt is { } rotatedAt && now - rotatedAt > RotationGrace)
        {
            await db.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            return null;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token.UserId && u.IsActive);
        if (user is null) return null;

        token.RotatedAt ??= now;
        return await StartAsync(user, token.FamilyId);
    }

    public async Task EndAsync()
    {
        var presented = http.HttpContext!.Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(presented))
        {
            var hash = Hash(presented);
            var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash)
                .Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync();
            if (familyId is not null) await RevokeWhereAsync(t => t.FamilyId == familyId);
        }
        http.HttpContext.Response.Cookies.Delete(RefreshCookie, CookieOptions(null));
    }

    /// <summary>
    /// Ends every session a user has and cancels their QR code and emailed links.
    /// Call after saving changes that should sign them out.
    /// </summary>
    public async Task RevokeAllAsync(int userId)
    {
        await RevokeWhereAsync(t => t.UserId == userId);
        await db.UserTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(int userId) =>
        await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new CurrentUserDto(
                u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber, u.Profile.Name,
                u.Profile.Permissions.Select(p => p.Permission).ToList(), u.MustChangePassword))
            .SingleAsync();

    private Task RevokeWhereAsync(System.Linq.Expressions.Expression<Func<RefreshToken, bool>> filter)
    {
        var now = DateTimeOffset.UtcNow;
        return db.RefreshTokens.Where(filter).Where(t => t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
    }

    private string CreateAccessToken(User user) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = DateTime.UtcNow + AccessTokenLifetime,
            SigningCredentials = new SigningCredentials(jwt.Key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(AppClaims.Subject, user.Id.ToString()),
                new Claim(AppClaims.Stamp, user.SecurityStamp.ToString()),
            ]),
        });

    private void SetCookie(string value, DateTimeOffset expires) =>
        http.HttpContext!.Response.Cookies.Append(RefreshCookie, value, CookieOptions(expires));

    private CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
    };

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class Passwords
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public static string GenerateTemporary() => RandomNumberGenerator.GetString(Alphabet, 16);

    public static void Set(this IPasswordHasher<User> hasher, User user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid();
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
    }
}
