using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;
using UserAdmin.Api.Email;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(
    AppDbContext db,
    SessionService sessions,
    IPasswordHasher<User> hasher,
    UserTokenService tokens,
    AccountMailer mailer) : ControllerBase
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SignUpLinkLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(1);

    /// <summary>Limits how often anyone can make us email a given address.</summary>
    private static readonly TimeSpan EmailCooldown = TimeSpan.FromMinutes(1);

    // Verified against when the email is unknown, so response time does not reveal which emails exist.
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(new User { Email = "", FirstName = "", LastName = "" }, Guid.NewGuid().ToString());

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<SessionResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        var hasPassword = user is { PasswordHash: not "" };
        var result = hasher.VerifyHashedPassword(user!, hasPassword ? user!.PasswordHash : DummyHash, request.Password);
        if (user is null || !hasPassword) return InvalidCredentials();

        var now = DateTimeOffset.UtcNow;
        if (user.LockoutEnd > now || !user.IsActive) return InvalidCredentials();

        if (result == PasswordVerificationResult.Failed)
        {
            if (++user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEnd = now + LockoutDuration;
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync();
            return InvalidCredentials();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        return await sessions.StartAsync(user);
    }

    /// <summary>
    /// Public sign-up. The account has no password until the emailed link is used, so nobody can
    /// claim an address they do not control. The response is the same whether or not the email is taken.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                ProfileId = await db.Profiles.Where(p => p.IsDefault).Select(p => p.Id).SingleAsync(),
            };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The same email signed up at the same moment, and that request sends the email.
                return Accepted();
            }
        }

        await SendPasswordEmailAsync(user, isReset: false);
        return Accepted();
    }

    /// <summary>The response is the same whether or not the email has an account.</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user is not null) await SendPasswordEmailAsync(user, isReset: true);
        return Accepted();
    }

    /// <summary>Sets a password from an emailed link, which finishes sign-up for a new account.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var token = await tokens.FindValidAsync(request.Token, UserTokenPurpose.SetPassword);
        if (token is null || !token.User.IsActive)
            return Problem(detail: "This link is invalid or has expired. Request a new one.", statusCode: StatusCodes.Status400BadRequest);

        var user = token.User;
        hasher.Set(user, request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await sessions.RevokeAllAsync(user.Id);
        return NoContent();
    }

    [HttpPost("qr-login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<SessionResponse>> QrLogin(QrLoginRequest request)
    {
        var token = await tokens.FindValidAsync(request.Token, UserTokenPurpose.QrLogin);
        var now = DateTimeOffset.UtcNow;
        if (token is null || !token.User.IsActive || token.User.LockoutEnd > now)
            return Problem(detail: "This QR code is not valid or has expired.", statusCode: StatusCodes.Status401Unauthorized);

        token.User.LastLoginAt = now;
        return await sessions.StartAsync(token.User);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<SessionResponse>> Refresh()
    {
        var session = await sessions.RefreshAsync();
        return session is null ? Unauthorized() : session;
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await sessions.EndAsync();
        return NoContent();
    }

    private async Task SendPasswordEmailAsync(User user, bool isReset)
    {
        if (!user.IsActive) return;
        if (await tokens.WasIssuedWithinAsync(user.Id, UserTokenPurpose.SetPassword, EmailCooldown)) return;

        if (user.PasswordHash == "")
        {
            mailer.SendFinishSignUp(user.Email, await tokens.IssueAsync(user.Id, UserTokenPurpose.SetPassword, SignUpLinkLifetime));
            return;
        }

        var token = await tokens.IssueAsync(user.Id, UserTokenPurpose.SetPassword, ResetLinkLifetime);
        if (isReset) mailer.SendPasswordReset(user.Email, token);
        else mailer.SendAlreadyRegistered(user.Email, token);
    }

    private ObjectResult InvalidCredentials() => Problem(
        detail: "Invalid email or password. Accounts are locked for 15 minutes after repeated failed attempts.",
        statusCode: StatusCodes.Status401Unauthorized);
}
