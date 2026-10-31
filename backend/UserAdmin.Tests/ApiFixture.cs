using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Email;

namespace UserAdmin.Tests;

/// <summary>Runs the real API against a throwaway SQL Server LocalDB database.</summary>
public class ApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string BootstrapPassword = "bootstrap-password-1";
    public const string AdminPassword = "admin-password-12345";

    private WebApplicationFactory<Program> _factory = null!;

    public CapturingEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        // Program reads these while building the host, so they are set as environment variables.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default",
            $@"Server=(localdb)\MSSQLLocalDB;Database=UserAdminTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", AdminEmail);
        Environment.SetEnvironmentVariable("Seed__AdminPassword", BootstrapPassword);
        Environment.SetEnvironmentVariable("RateLimiting__LoginPermitPerMinute", "100000");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails)));

        var client = NewClient();
        var session = await client.LoginAsync(AdminEmail, BootstrapPassword);
        client.UseToken(session.AccessToken);
        var changed = await client.PostAsJsonAsync("/api/me/password", new ChangePasswordRequest(BootstrapPassword, AdminPassword));
        changed.EnsureSuccessStatusCode();
    }

    public HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    public async Task ExpireTokensAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.UserTokens.Where(t => t.User.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, expired));
    }

    /// <summary>Lets the next email to this address through the per-address cooldown.</summary>
    public async Task SkipEmailCooldownAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-5);
        await db.UserTokens.Where(t => t.User.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, earlier));
    }

    public HttpClient NewClientWithoutCookies() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public async Task BackdateRotationsAsync(int userId, TimeSpan by)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RotatedAt != null).ToListAsync();
        foreach (var token in tokens) token.RotatedAt -= by;
        await db.SaveChangesAsync();
    }

    public async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient();
        client.UseToken((await client.LoginAsync(AdminEmail, AdminPassword)).AccessToken);
        return client;
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await _factory.DisposeAsync();
    }
}

public class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string email) => _sent.Where(m => m.To == email).ToList();

    /// <summary>Emails go out in the background, so this waits for the expected number to arrive.</summary>
    public async Task<EmailMessage> WaitForAsync(string email, int count = 1)
    {
        for (var i = 0; i < 100; i++)
        {
            var sent = To(email);
            if (sent.Count >= count) return sent[count - 1];
            await Task.Delay(50);
        }
        throw new TimeoutException($"Expected {count} email(s) to {email}, found {To(email).Count}.");
    }

    public static string TokenIn(EmailMessage message) =>
        Regex.Match(message.Body, "#token=([0-9A-Fa-f]{64})").Groups[1].Value;
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;

public static class TestClientExtensions
{
    public static void UseToken(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static async Task<SessionResponse> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
