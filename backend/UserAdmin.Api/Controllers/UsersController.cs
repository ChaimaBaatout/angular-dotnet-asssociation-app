using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(
    AppDbContext db,
    SessionService sessions,
    IPasswordHasher<User> hasher,
    UserTokenService tokens) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.UsersRead)]
    public async Task<PagedResult<UserDto>> List(
        string? search, int? profileId, bool? isActive, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.Email.Contains(term) || u.FirstName.Contains(term) || u.LastName.Contains(term));
        }
        if (profileId is not null) query = query.Where(u => u.ProfileId == profileId);
        if (isActive is not null) query = query.Where(u => u.IsActive == isActive);

        var total = await query.CountAsync();
        var items = await ToDto(query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
        return new PagedResult<UserDto>(items, total, page, pageSize);
    }

    [HttpGet("{id:int}")]
    [Authorize(Permissions.UsersRead)]
    public async Task<ActionResult<UserDto>> Get(int id)
    {
        var user = await ToDto(db.Users.AsNoTracking().Where(u => u.Id == id)).SingleOrDefaultAsync();
        return user is null ? NotFound() : user;
    }

    [HttpPost]
    [Authorize(Permissions.UsersWrite)]
    public async Task<ActionResult<CreatedUserResponse>> Create(SaveUserRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email)) return EmailTaken();
        if (await CheckProfileAssignableAsync(request.ProfileId) is { } denied) return denied;

        var user = new User
        {
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = NormalizePhone(request.PhoneNumber),
            ProfileId = request.ProfileId,
            IsActive = request.IsActive,
            MustChangePassword = true,
        };
        var temporaryPassword = Passwords.GenerateTemporary();
        hasher.Set(user, temporaryPassword);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dto = await ToDto(db.Users.AsNoTracking().Where(u => u.Id == user.Id)).SingleAsync();
        return CreatedAtAction(nameof(Get), new { id = user.Id }, new CreatedUserResponse(dto, temporaryPassword));
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.UsersWrite)]
    public async Task<ActionResult<UserDto>> Update(int id, SaveUserRequest request)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (await CheckCanManageAsync(user) is { } denied) return denied;

        var isSelf = id == User.GetUserId();
        if (isSelf && !request.IsActive) return Conflict("You cannot deactivate your own account.");
        if (isSelf && request.ProfileId != user.ProfileId) return Conflict("You cannot change your own profile.");

        var email = NormalizeEmail(request.Email);
        if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email)) return EmailTaken();
        if (request.ProfileId != user.ProfileId && await CheckProfileAssignableAsync(request.ProfileId) is { } notAssignable)
            return notAssignable;

        var deactivated = user.IsActive && !request.IsActive;
        var emailChanged = email != user.Email;
        user.Email = email;
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = NormalizePhone(request.PhoneNumber);
        user.ProfileId = request.ProfileId;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        if (deactivated) await sessions.RevokeAllAsync(id);
        // A link already sent to the old address must not keep working.
        if (emailChanged) await tokens.RemoveAsync(id, UserTokenPurpose.SetPassword);
        return await ToDto(db.Users.AsNoTracking().Where(u => u.Id == id)).SingleAsync();
    }

    /// <summary>Replaces the password with a temporary one, unlocks the account and signs the user out.</summary>
    [HttpPost("{id:int}/reset-password")]
    [Authorize(Permissions.UsersWrite)]
    public async Task<ActionResult<TemporaryPasswordResponse>> ResetPassword(int id)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (id == User.GetUserId()) return Conflict("Use the change password form for your own account.");
        if (await CheckCanManageAsync(user) is { } denied) return denied;

        var temporaryPassword = Passwords.GenerateTemporary();
        hasher.Set(user, temporaryPassword);
        user.MustChangePassword = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        await sessions.RevokeAllAsync(id);
        return new TemporaryPasswordResponse(temporaryPassword);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Permissions.UsersWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (id == User.GetUserId()) return Conflict("You cannot delete your own account.");
        if (await CheckCanManageAsync(user) is { } denied) return denied;

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Stops someone with user admin rights taking over or sidelining an account more privileged than their own.</summary>
    private async Task<ActionResult?> CheckCanManageAsync(User target)
    {
        var targetPermissions = await db.ProfilePermissions.Where(p => p.ProfileId == target.ProfileId)
            .Select(p => p.Permission).ToListAsync();
        return User.GetPermissions().IsSupersetOf(targetPermissions)
            ? null
            : Forbidden("This user has permissions you do not hold, so you cannot manage them.");
    }

    /// <summary>Stops someone with user admin rights handing out permissions they do not hold themselves.</summary>
    private async Task<ActionResult?> CheckProfileAssignableAsync(int profileId)
    {
        var profile = await db.Profiles.AsNoTracking().Include(p => p.Permissions)
            .SingleOrDefaultAsync(p => p.Id == profileId);
        if (profile is null)
        {
            ModelState.AddModelError(nameof(SaveUserRequest.ProfileId), "This profile does not exist.");
            return ValidationProblem(ModelState);
        }
        return User.GetPermissions().IsSupersetOf(profile.Permissions.Select(p => p.Permission))
            ? null
            : Forbidden("This profile has permissions you do not hold, so you cannot assign it.");
    }

    private ObjectResult Forbidden(string detail) => Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden);

    private ConflictObjectResult Conflict(string detail) =>
        base.Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail });

    private ConflictObjectResult EmailTaken() => Conflict("A user with this email already exists.");

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static IQueryable<UserDto> ToDto(IQueryable<User> query)
    {
        var now = DateTimeOffset.UtcNow;
        return query.Select(u => new UserDto(
            u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber, u.ProfileId, u.Profile.Name,
            u.IsActive, u.LockoutEnd != null && u.LockoutEnd > now, u.PasswordHash == "", u.MustChangePassword,
            u.LastLoginAt, u.CreatedAt, u.UpdatedAt));
    }
}
