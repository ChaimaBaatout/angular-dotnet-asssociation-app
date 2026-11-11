using System.ComponentModel.DataAnnotations;

namespace UserAdmin.Api.Contracts;

public record LoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);

public record ChangePasswordRequest(
    [Required, StringLength(128)] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = 5)] string NewPassword);

public record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName);

public record ForgotPasswordRequest([Required, EmailAddress, StringLength(256)] string Email);

public record ResetPasswordRequest(
    [Required, StringLength(128)] string Token,
    [Required, StringLength(128, MinimumLength = 5)] string NewPassword);

public record QrLoginRequest([Required, StringLength(128)] string Token);

public record CreateQrCodeRequest([Required, StringLength(128)] string CurrentPassword);

public record QrCodeStatus(bool Enabled, DateTimeOffset? ExpiresAt);

public record QrCodeResponse(string Token, DateTimeOffset ExpiresAt);

public record SessionResponse(string AccessToken, CurrentUserDto User);

public record CurrentUserDto(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string ProfileName,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public record UpdateMeRequest(
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [Phone, StringLength(30)] string? PhoneNumber);

public record UserDto(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    int ProfileId,
    string ProfileName,
    bool IsActive,
    bool IsLocked,
    bool IsPending,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record SaveUserRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [Phone, StringLength(30)] string? PhoneNumber,
    [Range(1, int.MaxValue)] int ProfileId,
    bool IsActive = true);

public record CreatedUserResponse(UserDto User, string TemporaryPassword);

public record TemporaryPasswordResponse(string TemporaryPassword);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record ProfileDto(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsDefault,
    IReadOnlyList<string> Permissions,
    int UserCount);

public record ProfileOptionDto(int Id, string Name);

public record SaveProfileRequest(
    [Required, StringLength(100)] string Name,
    [StringLength(500)] string? Description,
    [Required] IReadOnlyList<string> Permissions);

public record ContactDto(
    int Id,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address,
    string? City,
    string? TaxNumber,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A supplier or customer.</summary>
public record SaveContactRequest(
    [Required, StringLength(200)] string Name,
    [StringLength(200)] string? ContactName,
    [EmailAddress, StringLength(256)] string? Email,
    [Phone, StringLength(30)] string? Phone,
    [StringLength(300)] string? Address,
    [StringLength(100)] string? City,
    [StringLength(50)] string? TaxNumber,
    [StringLength(1000)] string? Notes,
    bool IsActive = true);

public record ContactOptionDto(int Id, string Name);

public record ArticleDto(
    int Id,
    string Reference,
    string Name,
    string? Brand,
    string? Category,
    string? Compatibility,
    string? Description,
    string? Location,
    int? SupplierId,
    string? SupplierName,
    decimal PurchasePrice,
    decimal SalePrice,
    int Quantity,
    int MinimumStock,
    bool IsLowStock,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record SaveArticleRequest(
    [Required, StringLength(50)] string Reference,
    [Required, StringLength(200)] string Name,
    [StringLength(100)] string? Brand,
    [StringLength(100)] string? Category,
    [StringLength(500)] string? Compatibility,
    [StringLength(1000)] string? Description,
    [StringLength(50)] string? Location,
    int? SupplierId,
    [Range(typeof(decimal), "0", "1000000000")] decimal PurchasePrice,
    [Range(typeof(decimal), "0", "1000000000")] decimal SalePrice,
    [Range(0, 1_000_000_000)] int Quantity,
    [Range(0, 1_000_000_000)] int MinimumStock,
    bool IsActive = true);

/// <summary>Stock figures for active articles. Stock value is at purchase price.</summary>
public record ArticleStatsDto(int Total, int LowStock, int OutOfStock, decimal StockValue);
