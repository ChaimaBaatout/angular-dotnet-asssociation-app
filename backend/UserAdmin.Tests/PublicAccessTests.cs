using System.Net;
using System.Net.Http.Json;
using UserAdmin.Api.Contracts;
using static UserAdmin.Tests.AuthTests;

namespace UserAdmin.Tests;

[Collection("api")]
public class PublicAccessTests(ApiFixture api)
{
    private const string NewPassword = "a-brand-new-password";

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@test.local";

    private async Task<string> SignUpAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "New", "Person"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return CapturingEmailSender.TokenIn(await api.Emails.WaitForAsync(email));
    }

    /// <summary>Signs up and confirms a new account, returning a client signed in to it.</summary>
    private async Task<(HttpClient Client, string Email)> NewAccountAsync()
    {
        var client = api.NewClient();
        var email = UniqueEmail();
        var token = await SignUpAsync(client, email);
        (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword))).EnsureSuccessStatusCode();
        client.UseToken((await client.LoginAsync(email, NewPassword)).AccessToken);
        return (client, email);
    }

    private async Task<string> CreateQrAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/me/qr-code", new CreateQrCodeRequest(NewPassword));
        return (await response.ReadAsync<QrCodeResponse>()).Token;
    }

    private Task<HttpResponseMessage> QrLoginAsync(string token) =>
        api.NewClient().PostAsJsonAsync("/api/auth/qr-login", new QrLoginRequest(token));

    [Fact]
    public async Task Sign_up_works_only_after_choosing_a_password_from_the_email()
    {
        var client = api.NewClient();
        var email = UniqueEmail();
        var token = await SignUpAsync(client, email);

        var tooEarly = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, tooEarly.StatusCode);

        (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword))).EnsureSuccessStatusCode();

        var session = await client.LoginAsync(email, NewPassword);
        Assert.Equal("Standard user", session.User.ProfileName);
        Assert.Empty(session.User.Permissions);
        Assert.False(session.User.MustChangePassword);
    }

    [Fact]
    public async Task Pending_sign_ups_appear_as_pending_to_administrators()
    {
        var email = UniqueEmail();
        await SignUpAsync(api.NewClient(), email);

        var admin = await api.AdminClientAsync();
        var found = await (await admin.GetAsync($"/api/users?search={email}")).ReadAsync<PagedResult<UserDto>>();
        Assert.True(found.Items.Single().IsPending);
    }

    [Fact]
    public async Task Sign_up_does_not_reveal_or_change_an_existing_account()
    {
        var (_, email) = await NewAccountAsync();
        await api.SkipEmailCooldownAsync(email);

        var response = await api.NewClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Someone", "Else"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var notice = await api.Emails.WaitForAsync(email, count: 2);
        Assert.Contains("already has an account", notice.Body);
        var session = await api.NewClient().LoginAsync(email, NewPassword);
        Assert.Equal("New", session.User.FirstName);
    }

    [Fact]
    public async Task Sign_up_rejects_invalid_details()
    {
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("not-an-email", "A", "B"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_responds_the_same_for_unknown_emails_and_sends_nothing()
    {
        var unknown = UniqueEmail();
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(unknown));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(200);
        Assert.Empty(api.Emails.To(unknown));
    }

    [Fact]
    public async Task Forgot_password_replaces_the_password_ends_sessions_and_the_link_works_once()
    {
        var (signedIn, email) = await NewAccountAsync();
        await api.SkipEmailCooldownAsync(email);
        const string replacement = "a-replacement-password";

        var client = api.NewClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = CapturingEmailSender.TokenIn(await api.Emails.WaitForAsync(email, count: 2));

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, replacement))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.PostAsync("/api/auth/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NewPassword))).StatusCode);
        await client.LoginAsync(email, replacement);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, "yet-another-password"))).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_clears_a_temporary_password_requirement_and_lockout()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var email = UniqueEmail();
        await admin.PostAsJsonAsync("/api/users", new SaveUserRequest(email, "A", "B", null, profileId));

        var client = api.NewClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong-password"));

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var token = CapturingEmailSender.TokenIn(await api.Emails.WaitForAsync(email));
        (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword))).EnsureSuccessStatusCode();

        Assert.False((await client.LoginAsync(email, NewPassword)).User.MustChangePassword);
    }

    [Fact]
    public async Task Password_emails_are_limited_per_address()
    {
        var email = UniqueEmail();
        var client = api.NewClient();
        await SignUpAsync(client, email);

        for (var i = 0; i < 3; i++)
            await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));

        await Task.Delay(200);
        Assert.Single(api.Emails.To(email));
    }

    [Fact]
    public async Task Reset_links_expire_and_reject_weak_passwords_and_made_up_tokens()
    {
        var client = api.NewClient();
        var email = UniqueEmail();
        var token = await SignUpAsync(client, email);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, "four"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(new string('a', 64), NewPassword))).StatusCode);

        await api.ExpireTokensAsync(email);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, NewPassword))).StatusCode);
    }

    [Fact]
    public async Task Default_profile_cannot_be_deleted()
    {
        var admin = await api.AdminClientAsync();
        var profiles = await (await admin.GetAsync("/api/profiles")).ReadAsync<List<ProfileDto>>();

        var response = await admin.DeleteAsync($"/api/profiles/{profiles.Single(p => p.IsDefault).Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Qr_code_signs_in_after_being_created_with_the_password()
    {
        var (client, email) = await NewAccountAsync();
        Assert.False((await (await client.GetAsync("/api/me/qr-code")).ReadAsync<QrCodeStatus>()).Enabled);

        var wrongPassword = await client.PostAsJsonAsync("/api/me/qr-code", new CreateQrCodeRequest("not-my-password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        var token = await CreateQrAsync(client);
        Assert.True((await (await client.GetAsync("/api/me/qr-code")).ReadAsync<QrCodeStatus>()).Enabled);

        var session = await (await QrLoginAsync(token)).ReadAsync<SessionResponse>();
        Assert.Equal(email, session.User.Email);

        var signedIn = api.NewClient();
        signedIn.UseToken(session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Each_account_has_its_own_qr_code()
    {
        var (first, firstEmail) = await NewAccountAsync();
        var (second, secondEmail) = await NewAccountAsync();

        var firstSession = await (await QrLoginAsync(await CreateQrAsync(first))).ReadAsync<SessionResponse>();
        var secondSession = await (await QrLoginAsync(await CreateQrAsync(second))).ReadAsync<SessionResponse>();

        Assert.Equal(firstEmail, firstSession.User.Email);
        Assert.Equal(secondEmail, secondSession.User.Email);
    }

    [Fact]
    public async Task Replacing_or_removing_a_qr_code_cancels_the_old_one()
    {
        var (client, _) = await NewAccountAsync();

        var old = await CreateQrAsync(client);
        var current = await CreateQrAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await QrLoginAsync(current)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/me/qr-code")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(current)).StatusCode);
    }

    [Fact]
    public async Task Qr_code_stops_working_when_expired_or_the_password_changes()
    {
        var (expiring, expiringEmail) = await NewAccountAsync();
        var expiredToken = await CreateQrAsync(expiring);
        await api.ExpireTokensAsync(expiringEmail);
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(expiredToken)).StatusCode);

        var (changing, _) = await NewAccountAsync();
        var token = await CreateQrAsync(changing);
        (await changing.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(NewPassword, "another-long-password"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Administrator_actions_cancel_a_users_qr_code()
    {
        var admin = await api.AdminClientAsync();

        var (reset, resetEmail) = await NewAccountAsync();
        var resetToken = await CreateQrAsync(reset);
        var resetUser = (await (await admin.GetAsync($"/api/users?search={resetEmail}")).ReadAsync<PagedResult<UserDto>>()).Items.Single();
        (await admin.PostAsync($"/api/users/{resetUser.Id}/reset-password", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(resetToken)).StatusCode);

        var (deactivated, deactivatedEmail) = await NewAccountAsync();
        var token = await CreateQrAsync(deactivated);
        var user = (await (await admin.GetAsync($"/api/users?search={deactivatedEmail}")).ReadAsync<PagedResult<UserDto>>()).Items.Single();
        (await admin.PutAsJsonAsync($"/api/users/{user.Id}",
            new SaveUserRequest(user.Email, user.FirstName, user.LastName, null, user.ProfileId, false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await QrLoginAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Temporary_password_accounts_cannot_create_a_qr_code()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await CreateProfileAsync(admin);
        var created = await (await admin.PostAsJsonAsync("/api/users",
            new SaveUserRequest(UniqueEmail(), "A", "B", null, profileId))).ReadAsync<CreatedUserResponse>();

        var client = api.NewClient();
        client.UseToken((await client.LoginAsync(created.User.Email, created.TemporaryPassword)).AccessToken);

        var response = await client.PostAsJsonAsync("/api/me/qr-code", new CreateQrCodeRequest(created.TemporaryPassword));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
