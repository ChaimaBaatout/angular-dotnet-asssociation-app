using UserAdmin.Api.Contracts;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

/// <summary>What the suppliers and customers controllers share, since both hold the same details.</summary>
internal static class Contacts
{
    public static IQueryable<T> Filter<T>(IQueryable<T> query, string? search, bool? isActive) where T : BusinessContact
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.ContactName!.Contains(term) || c.Email!.Contains(term)
                || c.Phone!.Contains(term) || c.City!.Contains(term) || c.TaxNumber!.Contains(term));
        }
        if (isActive is not null) query = query.Where(c => c.IsActive == isActive);
        return query;
    }

    public static IQueryable<ContactDto> ToDto<T>(IQueryable<T> query) where T : BusinessContact =>
        query.Select(c => new ContactDto(c.Id, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.City,
            c.TaxNumber, c.Notes, c.IsActive, c.CreatedAt, c.UpdatedAt));

    public static void Apply(BusinessContact contact, SaveContactRequest request)
    {
        contact.Name = request.Name.Trim();
        contact.ContactName = Input.Optional(request.ContactName);
        contact.Email = Input.Optional(request.Email)?.ToLowerInvariant();
        contact.Phone = Input.Optional(request.Phone);
        contact.Address = Input.Optional(request.Address);
        contact.City = Input.Optional(request.City);
        contact.TaxNumber = Input.Optional(request.TaxNumber);
        contact.Notes = Input.Optional(request.Notes);
        contact.IsActive = request.IsActive;
        contact.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

internal static class Input
{
    /// <summary>Trims text and stores blanks as null.</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
