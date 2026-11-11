using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.SuppliersRead)]
    public async Task<PagedResult<ContactDto>> List(string? search, bool? isActive, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = Contacts.Filter(db.Suppliers.AsNoTracking(), search, isActive);
        var total = await query.CountAsync();
        var items = await Contacts.ToDto(query.OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
        return new PagedResult<ContactDto>(items, total, page, pageSize);
    }

    /// <summary>Names only, so anyone who can view articles can see and choose their supplier.</summary>
    [HttpGet("options")]
    [Authorize(Permissions.ArticlesRead)]
    public async Task<IReadOnlyList<ContactOptionDto>> Options() =>
        await db.Suppliers.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new ContactOptionDto(s.Id, s.Name)).ToListAsync();

    [HttpGet("{id:int}")]
    [Authorize(Permissions.SuppliersRead)]
    public async Task<ActionResult<ContactDto>> Get(int id)
    {
        var supplier = await Contacts.ToDto(db.Suppliers.AsNoTracking().Where(s => s.Id == id)).SingleOrDefaultAsync();
        return supplier is null ? NotFound() : supplier;
    }

    [HttpPost]
    [Authorize(Permissions.SuppliersWrite)]
    public async Task<ActionResult<ContactDto>> Create(SaveContactRequest request)
    {
        if (await db.Suppliers.AnyAsync(s => s.Name == request.Name.Trim())) return NameTaken();

        var supplier = new Supplier { Name = "" };
        Contacts.Apply(supplier, request);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var dto = await Contacts.ToDto(db.Suppliers.AsNoTracking().Where(s => s.Id == supplier.Id)).SingleAsync();
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.SuppliersWrite)]
    public async Task<ActionResult<ContactDto>> Update(int id, SaveContactRequest request)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();
        if (await db.Suppliers.AnyAsync(s => s.Name == request.Name.Trim() && s.Id != id)) return NameTaken();

        Contacts.Apply(supplier, request);
        await db.SaveChangesAsync();
        return await Contacts.ToDto(db.Suppliers.AsNoTracking().Where(s => s.Id == id)).SingleAsync();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Permissions.SuppliersWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();

        var articles = await db.Articles.CountAsync(a => a.SupplierId == id);
        if (articles > 0)
        {
            return ConflictDetail(articles == 1
                ? "1 article uses this supplier. Change its supplier, or deactivate this supplier instead."
                : $"{articles} articles use this supplier. Change their supplier, or deactivate this supplier instead.");
        }

        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private ConflictObjectResult ConflictDetail(string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail });

    private ConflictObjectResult NameTaken() => ConflictDetail("A supplier with this name already exists.");
}
