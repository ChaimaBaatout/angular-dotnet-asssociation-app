using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.ProfilesRead)]
    public async Task<IReadOnlyList<ProfileDto>> List() =>
        await ToDto(db.Profiles.AsNoTracking().OrderBy(p => p.Name)).ToListAsync();

    /// <summary>Names only, so anyone who can view users can filter and assign without needing profile rights.</summary>
    [HttpGet("options")]
    [Authorize(Permissions.UsersRead)]
    public async Task<IReadOnlyList<ProfileOptionDto>> Options() =>
        await db.Profiles.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new ProfileOptionDto(p.Id, p.Name)).ToListAsync();

    [HttpGet("permissions")]
    [Authorize(Permissions.ProfilesRead)]
    public IReadOnlyList<PermissionInfo> PermissionCatalog() => Permissions.Catalog;

    [HttpGet("{id:int}")]
    [Authorize(Permissions.ProfilesRead)]
    public async Task<ActionResult<ProfileDto>> Get(int id)
    {
        var profile = await ToDto(db.Profiles.AsNoTracking().Where(p => p.Id == id)).SingleOrDefaultAsync();
        return profile is null ? NotFound() : profile;
    }

    [HttpPost]
    [Authorize(Permissions.ProfilesWrite)]
    public async Task<ActionResult<ProfileDto>> Create(SaveProfileRequest request)
    {
        var name = request.Name.Trim();
        var permissions = request.Permissions.Distinct().ToList();
        if (await CheckPermissionsAsync(permissions) is { } invalid) return invalid;
        if (await db.Profiles.AnyAsync(p => p.Name == name)) return NameTaken();

        var profile = new Profile
        {
            Name = name,
            Description = NormalizeDescription(request.Description),
            Permissions = permissions.Select(p => new ProfilePermission { Permission = p }).ToList(),
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();

        var dto = await ToDto(db.Profiles.AsNoTracking().Where(p => p.Id == profile.Id)).SingleAsync();
        return CreatedAtAction(nameof(Get), new { id = profile.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.ProfilesWrite)]
    public async Task<ActionResult<ProfileDto>> Update(int id, SaveProfileRequest request)
    {
        var profile = await db.Profiles.Include(p => p.Permissions).SingleOrDefaultAsync(p => p.Id == id);
        if (profile is null) return NotFound();
        if (profile.IsSystem) return ConflictDetail("System profiles cannot be changed.");

        var name = request.Name.Trim();
        var permissions = request.Permissions.Distinct().ToList();
        if (await CheckPermissionsAsync(permissions) is { } invalid) return invalid;
        if (CheckHeld(profile.Permissions.Select(p => p.Permission)) is { } denied) return denied;
        if (name != profile.Name && await db.Profiles.AnyAsync(p => p.Name == name && p.Id != id)) return NameTaken();

        profile.Name = name;
        profile.Description = NormalizeDescription(request.Description);
        profile.Permissions.RemoveAll(p => !permissions.Contains(p.Permission));
        var existing = profile.Permissions.Select(p => p.Permission).ToHashSet();
        profile.Permissions.AddRange(permissions.Where(p => !existing.Contains(p))
            .Select(p => new ProfilePermission { Permission = p }));
        await db.SaveChangesAsync();

        return await ToDto(db.Profiles.AsNoTracking().Where(p => p.Id == id)).SingleAsync();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Permissions.ProfilesWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var profile = await db.Profiles.Include(p => p.Permissions).SingleOrDefaultAsync(p => p.Id == id);
        if (profile is null) return NotFound();
        if (profile.IsSystem) return ConflictDetail("System profiles cannot be deleted.");
        if (profile.IsDefault) return ConflictDetail("This profile is given to people who sign up, so it cannot be deleted.");
        if (CheckHeld(profile.Permissions.Select(p => p.Permission)) is { } denied) return denied;
        if (await db.Users.AnyAsync(u => u.ProfileId == id))
            return ConflictDetail("This profile is assigned to users. Move them to another profile first.");

        db.Profiles.Remove(profile);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<ActionResult?> CheckPermissionsAsync(IReadOnlyList<string> permissions)
    {
        var unknown = permissions.Where(p => !Permissions.All.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            ModelState.AddModelError(nameof(SaveProfileRequest.Permissions), $"Unknown permissions: {string.Join(", ", unknown)}.");
            return Task.FromResult<ActionResult?>(ValidationProblem(ModelState));
        }
        return Task.FromResult(CheckHeld(permissions));
    }

    /// <summary>Stops someone with profile admin rights granting themselves, or anyone else, permissions they do not hold.</summary>
    private ActionResult? CheckHeld(IEnumerable<string> permissions) =>
        User.GetPermissions().IsSupersetOf(permissions)
            ? null
            : Problem(detail: "You can only manage permissions you hold yourself.", statusCode: StatusCodes.Status403Forbidden);

    private ConflictObjectResult ConflictDetail(string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail });

    private ConflictObjectResult NameTaken() => ConflictDetail("A profile with this name already exists.");

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static IQueryable<ProfileDto> ToDto(IQueryable<Profile> query) =>
        query.Select(p => new ProfileDto(
            p.Id, p.Name, p.Description, p.IsSystem, p.IsDefault,
            p.Permissions.Select(x => x.Permission).ToList(), p.Users.Count));
}
