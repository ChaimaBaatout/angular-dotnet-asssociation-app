using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;
using UserAdmin.Api.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<UserTokenService>();
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<AccountMailer>();
builder.Services.AddHostedService<EmailDispatcher>();

// Without a mail server, Development prints emails to the console. Anywhere else that would
// silently lose password reset emails, so the app refuses to start.
if (!string.IsNullOrEmpty(builder.Configuration["Email:SmtpHost"]))
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Email:SmtpHost must be configured.");
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
}
if (!builder.Environment.IsDevelopment() && string.IsNullOrEmpty(builder.Configuration["App:BaseUrl"]))
    throw new InvalidOperationException("App:BaseUrl must be configured.");

// Outside Development a signing key must be configured. In Development a random one is enough:
// a restart just means browsers silently get a new access token from their refresh cookie.
var signingKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(signingKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key must be configured (at least 32 bytes).");
    signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
if (System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
var jwt = new JwtSettings(
    new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)),
    builder.Configuration["Jwt:Issuer"] ?? "UserAdmin",
    builder.Configuration["Jwt:Audience"] ?? "UserAdmin");
builder.Services.AddSingleton(jwt);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.Key,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            // Permissions and account state are read from the database on every request, so
            // deactivating a user or editing a profile takes effect immediately, not at token expiry.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                var userId = principal.GetUserId();
                var stamp = Guid.Parse(principal.FindFirstValue(AppClaims.Stamp)!);
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                var state = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive && u.SecurityStamp == stamp)
                    .Select(u => new
                    {
                        u.MustChangePassword,
                        Permissions = u.Profile.Permissions.Select(p => p.Permission).ToList(),
                    })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (state is null)
                {
                    context.Fail("The session is no longer valid.");
                    return;
                }

                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaims(state.Permissions.Select(p => new Claim(AppClaims.Permission, p)));
                if (state.MustChangePassword) identity.AddClaim(new Claim(AppClaims.MustChangePassword, "true"));
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Every endpoint needs a signed-in user unless it opts out with [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy => policy
            .RequireClaim(AppClaims.Permission, permission)
            .RequireAssertion(context => !context.User.HasClaim(c => c.Type == AppClaims.MustChangePassword)));
    }
});

var loginPermitPerMinute = builder.Configuration.GetValue("RateLimiting:LoginPermitPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

await DbSeeder.MigrateAndSeedAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
