using System.Net;
using System.Net.Http.Json;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;

namespace UserAdmin.Tests;

[Collection("api")]
public class AuthTests(ApiFixture api)
{
    private static string UniqueEmail() => $"{Guid.NewGuid():N}@test.local";

    /// <summary>Creates a user on the given profile who has already replaced their temporary password.</summary>
    internal static async Task<(HttpClient Client, UserDto User, string Password)> CreateSignedInUserAsync(
        ApiFixture api, HttpClient admin, int profileId)
    {
        var email = UniqueEmail();
        var created = await (await admin.PostAsJsonAsync("/api/users",
            new SaveUserRequest(email, "Test", "User", null, profileId))).ReadAsync<CreatedUserResponse>();

        const string password = "a-good-long-password";
        var client = api.NewClient();
        client.UseToken((await client.LoginAsync(email, created.TemporaryPassword)).AccessToken);
        var session = await (await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(created.TemporaryPassword, password))).ReadAsync<SessionResponse>();
        client.UseToken(session.AccessToken);
        return (client, created.User, password);
    }

    internal static async Task<int> CreateProfileAsync(HttpClient admin, params string[] permissions)
    {
        var profile = await (await admin.PostAsJsonAsync("/api/profiles",
            new SaveProfileRequest($"Profile {Guid.NewGuid():N}", null, permissions))).ReadAsync<ProfileDto>();
        return profile.Id;
    }

    [Fact]
    public async Task Endpoints_require_authentication()
    {
        var client = api.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profiles")).StatusCode);
    }

    [Fact]
    public async Task Login_rejects_wrong_password_and_unknown_email_identically()
    {
        var client = api.NewClient();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFixture.AdminEmail, "wrong-password"));
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(UniqueEmail(), "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Login_email_is_case_insensitive()
    {
        var session = await api.NewClient().LoginAsync(ApiFixture.AdminEmail.ToUpperInvariant(), ApiFixture.AdminPassword);
        Assert.Contains(Permissions.UsersWrite, session.User.Permissions);
    }

    [Fact]
    public async Task Refresh_cookie_is_http_only_and_scoped()
    {
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFixture.AdminEmail, ApiFixture.AdminPassword));

        var cookie = response.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);
    }

    [Fact]
    public async Task Refresh_issues_a_working_token_and_logout_ends_the_session()
    {
        var client = await api.AdminClientAsync();

        var refreshed = await (await client.PostAsync("/api/auth/refresh", null)).ReadAsync<SessionResponse>();
        client.UseToken(refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_and_reset_unlocks_it()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (_, user, password) = await CreateSignedInUserAsync(api, admin, profileId);

        var client = api.NewClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "wrong-password"));

        var locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.True((await (await admin.GetAsync($"/api/users/{user.Id}")).ReadAsync<UserDto>()).IsLocked);

        var reset = await (await admin.PostAsync($"/api/users/{user.Id}/reset-password", null)).ReadAsync<TemporaryPasswordResponse>();
        var session = await client.LoginAsync(user.Email, reset.TemporaryPassword);
        Assert.True(session.User.MustChangePassword);
    }

    [Fact]
    public async Task Temporary_password_blocks_permissioned_endpoints_until_changed()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin, Permissions.UsersRead);
        var created = await (await admin.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "New", "User", null, profileId))).ReadAsync<CreatedUserResponse>();

        var client = api.NewClient();
        var session = await client.LoginAsync(created.User.Email, created.TemporaryPassword);
        client.UseToken(session.AccessToken);
        Assert.True(session.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        var changed = await (await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(created.TemporaryPassword, "a-good-long-password"))).ReadAsync<SessionResponse>();
        client.UseToken(changed.AccessToken);
        Assert.False(changed.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Change_password_checks_current_and_invalidates_other_sessions()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (client, user, password) = await CreateSignedInUserAsync(api, admin, profileId);

        var otherDevice = api.NewClient();
        otherDevice.UseToken((await otherDevice.LoginAsync(user.Email, password)).AccessToken);

        var wrongCurrent = await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest("not-my-password", "another-long-password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        var tooShort = await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(password, "four"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var session = await (await client.PostAsJsonAsync("/api/me/password",
            new ChangePasswordRequest(password, "another-long-password"))).ReadAsync<SessionResponse>();
        client.UseToken(session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("aaaaa")]
    [InlineData("!@#$%")]
    [InlineData("é ñ 1")]
    public async Task Any_password_of_five_characters_is_accepted(string newPassword)
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (client, user, password) = await CreateSignedInUserAsync(api, admin, profileId);

        (await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(password, newPassword))).EnsureSuccessStatusCode();

        await api.NewClient().LoginAsync(user.Email, newPassword);
    }

    [Fact]
    public async Task Users_can_update_their_own_details()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (client, _, _) = await CreateSignedInUserAsync(api, admin, profileId);

        var me = await (await client.PutAsJsonAsync("/api/me", new UpdateMeRequest("Renamed", "Person", "+216 20 000 000"))).ReadAsync<CurrentUserDto>();

        Assert.Equal("Renamed", me.FirstName);
        Assert.Equal("+216 20 000 000", me.PhoneNumber);
    }

    private static string RefreshCookieFrom(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

    private static HttpRequestMessage RefreshRequest(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_is_allowed_within_grace_then_revokes_the_family()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var (_, user, password) = await CreateSignedInUserAsync(api, admin, profileId);

        // No cookie jar, so each request presents exactly the token we choose.
        var client = api.NewClientWithoutCookies();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        var stolen = RefreshCookieFrom(login);

        var rotated = await client.SendAsync(RefreshRequest(stolen));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var current = RefreshCookieFrom(rotated);

        // Immediately afterwards it counts as a second tab, not an attack.
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(RefreshRequest(stolen))).StatusCode);

        await api.BackdateRotationsAsync(user.Id, TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshRequest(stolen))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(RefreshRequest(current))).StatusCode);
    }
}
