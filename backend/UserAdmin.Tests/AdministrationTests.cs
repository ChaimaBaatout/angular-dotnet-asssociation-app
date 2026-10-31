using System.Net;
using System.Net.Http.Json;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using static UserAdmin.Tests.AuthTests;

namespace UserAdmin.Tests;

[Collection("api")]
public class AdministrationTests(ApiFixture api)
{
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@test.local";

    private static async Task<int> AdministratorProfileIdAsync(HttpClient admin)
    {
        var options = await (await admin.GetAsync("/api/profiles/options")).ReadAsync<List<ProfileOptionDto>>();
        return options.Single(o => o.Name == "Administrator").Id;
    }

    private static SaveUserRequest ToRequest(UserDto u, int? profileId = null, bool? isActive = null, string? email = null) =>
        new(email ?? u.Email, u.FirstName, u.LastName, u.PhoneNumber, profileId ?? u.ProfileId, isActive ?? u.IsActive);

    [Fact]
    public async Task Admin_can_create_search_update_and_delete_a_user()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var email = UniqueEmail();

        var created = await admin.PostAsJsonAsync("/api/users", new SaveUserRequest(email.ToUpperInvariant(), "Ada", "Lovelace", null, profileId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.ReadAsync<CreatedUserResponse>();
        Assert.Equal(email, body.User.Email);
        Assert.True(body.User.MustChangePassword);
        Assert.Equal(16, body.TemporaryPassword.Length);

        var found = await (await admin.GetAsync($"/api/users?search={email}")).ReadAsync<PagedResult<UserDto>>();
        Assert.Equal(body.User.Id, found.Items.Single().Id);
        var byProfile = await (await admin.GetAsync($"/api/users?profileId={profileId}&isActive=true")).ReadAsync<PagedResult<UserDto>>();
        Assert.Equal(1, byProfile.Total);

        var updated = await (await admin.PutAsJsonAsync($"/api/users/{body.User.Id}",
            ToRequest(body.User) with { FirstName = "Augusta" })).ReadAsync<UserDto>();
        Assert.Equal("Augusta", updated.FirstName);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{body.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/users/{body.User.Id}")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_conflicts_and_invalid_input_is_rejected()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);

        var duplicate = await admin.PostAsJsonAsync("/api/users", new SaveUserRequest(ApiFixture.AdminEmail, "A", "B", null, profileId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var badEmail = await admin.PostAsJsonAsync("/api/users", new SaveUserRequest("not-an-email", "A", "B", null, profileId));
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);

        var badProfile = await admin.PostAsJsonAsync("/api/users", new SaveUserRequest(UniqueEmail(), "A", "B", null, 999999));
        Assert.Equal(HttpStatusCode.BadRequest, badProfile.StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_access_immediately()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin, Permissions.UsersRead);
        var (client, user, password) = await CreateSignedInUserAsync(api, admin, profileId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);

        var dto = await (await admin.GetAsync($"/api/users/{user.Id}")).ReadAsync<UserDto>();
        (await admin.PutAsJsonAsync($"/api/users/{user.Id}", ToRequest(dto, isActive: false))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
        var login = await api.NewClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Editing_a_profile_changes_permissions_immediately()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (client, _, _) = await CreateSignedInUserAsync(api, admin, profileId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        var profile = await (await admin.GetAsync($"/api/profiles/{profileId}")).ReadAsync<ProfileDto>();
        (await admin.PutAsJsonAsync($"/api/profiles/{profileId}",
            new SaveProfileRequest(profile.Name, profile.Description, [Permissions.UsersRead]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "A", "B", null, profileId))).StatusCode);

        (await admin.PutAsJsonAsync($"/api/profiles/{profileId}",
            new SaveProfileRequest(profile.Name, profile.Description, []))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves_out()
    {
        var admin = await api.AdminClientAsync();
        var me = await (await admin.GetAsync("/api/me")).ReadAsync<CurrentUserDto>();
        var dto = await (await admin.GetAsync($"/api/users/{me.Id}")).ReadAsync<UserDto>();
        var otherProfile = await CreateProfileAsync(admin);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{me.Id}", ToRequest(dto, isActive: false))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{me.Id}", ToRequest(dto, profileId: otherProfile))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/users/{me.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{me.Id}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task User_admins_cannot_escalate_or_touch_more_privileged_accounts()
    {
        var admin = await api.AdminClientAsync();
        var adminProfile = await AdministratorProfileIdAsync(admin);
        var managerProfile = await CreateProfileAsync(admin, Permissions.UsersRead, Permissions.UsersWrite);
        var (manager, managerUser, _) = await CreateSignedInUserAsync(api, admin, managerProfile);
        var me = await (await admin.GetAsync("/api/me")).ReadAsync<CurrentUserDto>();
        var adminDto = await (await admin.GetAsync($"/api/users/{me.Id}")).ReadAsync<UserDto>();

        // Cannot create an administrator, or promote someone to one.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "A", "B", null, adminProfile))).StatusCode);
        var peer = await (await manager.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "P", "Q", null, managerProfile))).ReadAsync<CreatedUserResponse>();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/users/{peer.User.Id}",
            ToRequest(peer.User, profileId: adminProfile))).StatusCode);

        // Cannot take over or remove an administrator.
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/users/{me.Id}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsJsonAsync($"/api/users/{me.Id}",
            ToRequest(adminDto, email: managerUser.Email + ".x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/users/{me.Id}")).StatusCode);
    }

    [Fact]
    public async Task Profile_admins_cannot_grant_permissions_they_do_not_hold()
    {
        var admin = await api.AdminClientAsync();
        var profileAdminProfile = await CreateProfileAsync(admin, Permissions.ProfilesRead, Permissions.ProfilesWrite);
        var (profileAdmin, _, _) = await CreateSignedInUserAsync(api, admin, profileAdminProfile);

        Assert.Equal(HttpStatusCode.Forbidden, (await profileAdmin.PostAsJsonAsync("/api/profiles",
            new SaveProfileRequest($"P {Guid.NewGuid():N}", null, [Permissions.UsersWrite]))).StatusCode);

        var own = await (await admin.GetAsync($"/api/profiles/{profileAdminProfile}")).ReadAsync<ProfileDto>();
        Assert.Equal(HttpStatusCode.Forbidden, (await profileAdmin.PutAsJsonAsync($"/api/profiles/{profileAdminProfile}",
            new SaveProfileRequest(own.Name, null, [Permissions.ProfilesRead, Permissions.ProfilesWrite, Permissions.UsersWrite]))).StatusCode);

        var moreCapable = await CreateProfileAsync(admin, Permissions.UsersWrite);
        Assert.Equal(HttpStatusCode.Forbidden, (await profileAdmin.DeleteAsync($"/api/profiles/{moreCapable}")).StatusCode);
    }

    [Fact]
    public async Task Profile_rules_protect_system_and_assigned_profiles()
    {
        var admin = await api.AdminClientAsync();
        var adminProfile = await AdministratorProfileIdAsync(admin);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/profiles/{adminProfile}",
            new SaveProfileRequest("Renamed", null, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/profiles/{adminProfile}")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/profiles",
            new SaveProfileRequest($"P {Guid.NewGuid():N}", null, ["made.up"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/profiles",
            new SaveProfileRequest("Administrator", null, []))).StatusCode);

        var profileId = await CreateProfileAsync(admin);
        var created = await (await admin.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "A", "B", null, profileId))).ReadAsync<CreatedUserResponse>();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/profiles/{profileId}")).StatusCode);

        (await admin.DeleteAsync($"/api/users/{created.User.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/profiles/{profileId}")).StatusCode);
    }

    [Fact]
    public async Task Permission_catalog_is_available_to_profile_readers()
    {
        var admin = await api.AdminClientAsync();
        var catalog = await (await admin.GetAsync("/api/profiles/permissions")).ReadAsync<List<PermissionInfo>>();
        Assert.Contains(catalog, p => p.Code == Permissions.ProfilesWrite);
    }
}
