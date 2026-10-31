using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(
    AppDbContext db,
    SessionService sessions,
    IPasswordHasher<User> hasher,
    UserTokenService tokens) : ControllerBase
{
    private static readonly TimeSpan QrCodeLifetime = TimeSpan.FromDays(30);

    [HttpGet]
    public Task<CurrentUserDto> Get() => sessions.GetCurrentUserAsync(User.GetUserId());

    [HttpPut]
    public async Task<CurrentUserDto> Update(UpdateMeRequest request)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.GetUserId());
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return await sessions.GetCurrentUserAsync(user.Id);
    }

    /// <summary>Changes the password, signs out all other sessions and returns a fresh one for this browser.</summary>
    [HttpPost("password")]
    public async Task<ActionResult<SessionResponse>> ChangePassword(ChangePasswordRequest request)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.GetUserId());

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return ValidationError(nameof(request.CurrentPassword), "The current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword)
            return ValidationError(nameof(request.NewPassword), "The new password must be different from the current one.");

        hasher.Set(user, request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await sessions.RevokeAllAsync(user.Id);
        return await sessions.StartAsync(user);
    }

    [HttpGet("qr-code")]
    public async Task<QrCodeStatus> GetQrCode()
    {
        var token = await tokens.GetValidForUserAsync(User.GetUserId(), UserTokenPurpose.QrLogin);
        return new QrCodeStatus(token is not null, token?.ExpiresAt);
    }

    /// <summary>
    /// Creates the account's sign-in QR code, replacing any earlier one. The secret cannot be shown again.
    /// Only the owner can do this: an administrator doing it could sign in as them unnoticed.
    /// Asking for the password stops a briefly unattended session from leaving a lasting way in.
    /// </summary>
    [HttpPost("qr-code")]
    public async Task<ActionResult<QrCodeResponse>> CreateQrCode(CreateQrCodeRequest request)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.GetUserId());
        if (user.MustChangePassword)
            return Problem(detail: "Replace your temporary password first.", statusCode: StatusCodes.Status403Forbidden);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return ValidationError(nameof(request.CurrentPassword), "The current password is incorrect.");

        var raw = await tokens.IssueAsync(user.Id, UserTokenPurpose.QrLogin, QrCodeLifetime);
        return new QrCodeResponse(raw, DateTimeOffset.UtcNow + QrCodeLifetime);
    }

    [HttpDelete("qr-code")]
    public async Task<IActionResult> DeleteQrCode()
    {
        await tokens.RemoveAsync(User.GetUserId(), UserTokenPurpose.QrLogin);
        return NoContent();
    }

    private ActionResult ValidationError(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
