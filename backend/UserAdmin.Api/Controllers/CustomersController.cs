using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.CustomersRead)]
    public async Task<PagedResult<ContactDto>> List(string? search, bool? isActive, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = Contacts.Filter(db.Customers.AsNoTracking(), search, isActive);
        var total = await query.CountAsync();
        var items = await Contacts.ToDto(query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
        return new PagedResult<ContactDto>(items, total, page, pageSize);
    }

    [HttpGet("{id:int}")]
    [Authorize(Permissions.CustomersRead)]
    public async Task<ActionResult<ContactDto>> Get(int id)
    {
        var customer = await Contacts.ToDto(db.Customers.AsNoTracking().Where(c => c.Id == id)).SingleOrDefaultAsync();
        return customer is null ? NotFound() : customer;
    }

    [HttpPost]
    [Authorize(Permissions.CustomersWrite)]
    public async Task<ActionResult<ContactDto>> Create(SaveContactRequest request)
    {
        if (await db.Customers.AnyAsync(c => c.Name == request.Name.Trim())) return NameTaken();

        var customer = new Customer { Name = "" };
        Contacts.Apply(customer, request);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var dto = await Contacts.ToDto(db.Customers.AsNoTracking().Where(c => c.Id == customer.Id)).SingleAsync();
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.CustomersWrite)]
    public async Task<ActionResult<ContactDto>> Update(int id, SaveContactRequest request)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();
        if (await db.Customers.AnyAsync(c => c.Name == request.Name.Trim() && c.Id != id)) return NameTaken();

        Contacts.Apply(customer, request);
        await db.SaveChangesAsync();
        return await Contacts.ToDto(db.Customers.AsNoTracking().Where(c => c.Id == id)).SingleAsync();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Permissions.CustomersWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();

        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private ConflictObjectResult NameTaken() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = "A customer with this name already exists.",
        });
}
