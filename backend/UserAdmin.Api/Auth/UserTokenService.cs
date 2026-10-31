using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Auth;

public class UserTokenService(AppDbContext db)
{
    /// <summary>Creates a token, replacing any earlier one of this purpose. The result is the only copy of the secret.</summary>
    public async Task<string> IssueAsync(int userId, UserTokenPurpose purpose, TimeSpan lifetime)
    {
        var raw = RandomNumberGenerator.GetHexString(64);
        var now = DateTimeOffset.UtcNow;

        var token = await db.UserTokens.SingleOrDefaultAsync(t => t.UserId == userId && t.Purpose == purpose);
        if (token is null)
        {
            token = new UserToken { UserId = userId, Purpose = purpose, TokenHash = "" };
            db.UserTokens.Add(token);
        }
        token.TokenHash = SessionService.Hash(raw);
        token.CreatedAt = now;
        token.ExpiresAt = now + lifetime;
        await db.SaveChangesAsync();
        return raw;
    }

    /// <summary>Finds an unexpired token, with its user, from the secret someone presented.</summary>
    public Task<UserToken?> FindValidAsync(string raw, UserTokenPurpose purpose)
    {
        var hash = SessionService.Hash(raw);
        var now = DateTimeOffset.UtcNow;
        return db.UserTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose && t.ExpiresAt > now);
    }

    public Task<UserToken?> GetValidForUserAsync(int userId, UserTokenPurpose purpose)
    {
        var now = DateTimeOffset.UtcNow;
        return db.UserTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.UserId == userId && t.Purpose == purpose && t.ExpiresAt > now);
    }

    public Task<bool> WasIssuedWithinAsync(int userId, UserTokenPurpose purpose, TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;
        return db.UserTokens.AnyAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt > since);
    }

    public Task RemoveAsync(int userId, UserTokenPurpose purpose) =>
        db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose).ExecuteDeleteAsync();
}
