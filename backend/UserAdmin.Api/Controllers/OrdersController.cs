using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(AppDbContext db) : ControllerBase
{
    /// <summary>Applied to new orders. Saved orders keep the rate they were created with.</summary>
    public const decimal VatRate = 0.19m;

    [HttpGet]
    [Authorize(Permissions.OrdersRead)]
    public async Task<PagedResult<OrderSummaryDto>> List(
        string? search, PaymentStatus? paymentStatus, int? customerId, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matches the customer's name, or the order number with or without its ORD- prefix and zeros.
            var term = search.Trim();
            var digits = term.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase) ? term[4..] : term;
            query = int.TryParse(digits, out var id)
                ? query.Where(o => o.Id == id || o.Customer!.Name.Contains(term))
                : query.Where(o => o.Customer!.Name.Contains(term));
        }
        if (paymentStatus is not null) query = query.Where(o => o.PaymentStatus == paymentStatus);
        if (customerId is not null) query = query.Where(o => o.CustomerId == customerId);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderDate, o.CustomerId, o.Customer != null ? o.Customer.Name : null, o.Lines.Count,
                o.Total, o.PaymentStatus, o.AmountPaid, o.Total - o.AmountPaid, o.CreatedAt))
            .ToListAsync();
        return new PagedResult<OrderSummaryDto>(items, total, page, pageSize);
    }

    [HttpGet("stats")]
    [Authorize(Permissions.OrdersRead)]
    public async Task<OrderStatsDto> Stats()
    {
        var outstanding = db.Orders.AsNoTracking().Where(o => o.PaymentStatus != PaymentStatus.Paid);
        return new OrderStatsDto(await outstanding.CountAsync(), await outstanding.SumAsync(o => o.Total - o.AmountPaid));
    }

    [HttpGet("{id:int}")]
    [Authorize(Permissions.OrdersRead)]
    public async Task<ActionResult<OrderDto>> Get(int id)
    {
        var order = await LoadDtoAsync(id);
        return order is null ? NotFound() : order;
    }

    [HttpPost]
    [Authorize(Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderDto>> Create(SaveOrderRequest request)
    {
        var order = new Order { VatRate = VatRate };
        if (await SaveAsync(order, request, []) is { } failed) return failed;
        return CreatedAtAction(nameof(Get), new { id = order.Id }, await LoadDtoAsync(order.Id));
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderDto>> Update(int id, SaveOrderRequest request)
    {
        var order = await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        if (await SaveAsync(order, request, order.Lines.ToList()) is { } failed) return failed;
        return (await LoadDtoAsync(id))!;
    }

    [HttpPut("{id:int}/payment")]
    [Authorize(Permissions.OrdersWrite)]
    public async Task<ActionResult<OrderDto>> UpdatePayment(int id, SavePaymentRequest request)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        if (ApplyPayment(order, request.PaymentStatus, request.AmountPaid) is { } invalid) return invalid;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return (await LoadDtoAsync(id))!;
    }

    /// <summary>Deletes the order and puts its quantities back in stock.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Permissions.OrdersWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await ReturnStockAsync(order.Lines);
        db.Orders.Remove(order);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return NoContent();
    }

    /// <summary>
    /// Replaces the order's details and lines, and moves stock to match: the old lines go back into stock, then the
    /// new ones come out. Stock changes are single conditional updates in one transaction, so two orders saved at
    /// the same time cannot both take the last units.
    /// </summary>
    private async Task<ActionResult?> SaveAsync(Order order, SaveOrderRequest request, IReadOnlyList<OrderLine> oldLines)
    {
        if (request.OrderDate.Year < 2000) ModelState.AddModelError(nameof(request.OrderDate), "Enter the order date.");
        if (request.CustomerId is not null && !await db.Customers.AnyAsync(c => c.Id == request.CustomerId))
            ModelState.AddModelError(nameof(request.CustomerId), "This customer does not exist.");

        var articleIds = request.Lines.Select(l => l.ArticleId).Distinct().ToList();
        var articles = await db.Articles.AsNoTracking().Where(a => articleIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Reference, a.Name, a.IsActive })
            .ToDictionaryAsync(a => a.Id);
        var alreadyOnOrder = oldLines.Select(l => l.ArticleId).ToHashSet();
        foreach (var articleId in articleIds)
        {
            if (!articles.TryGetValue(articleId, out var article))
                ModelState.AddModelError(nameof(request.Lines), "An article on this order no longer exists.");
            else if (!article.IsActive && !alreadyOnOrder.Contains(articleId))
                ModelState.AddModelError(nameof(request.Lines), $"{article.Reference} is inactive, so it cannot be added.");
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        await using var transaction = await db.Database.BeginTransactionAsync();
        await ReturnStockAsync(oldLines);
        foreach (var taken in request.Lines.GroupBy(l => l.ArticleId))
        {
            var quantity = taken.Sum(l => l.Quantity);
            var updated = await db.Articles.Where(a => a.Id == taken.Key && a.Quantity >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Quantity, a => a.Quantity - quantity));
            if (updated == 0)
            {
                // Rolled back when the transaction is disposed, which also restores the old lines' stock.
                var available = await db.Articles.Where(a => a.Id == taken.Key).Select(a => a.Quantity).SingleAsync();
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Not enough stock",
                    Detail = $"Only {available} of {articles[taken.Key].Reference} {(available == 1 ? "is" : "are")} in stock.",
                });
            }
        }

        order.OrderDate = request.OrderDate;
        order.CustomerId = request.CustomerId;
        order.Notes = Input.Optional(request.Notes);
        order.Lines.Clear();
        order.Lines.AddRange(request.Lines.Select(l => new OrderLine
        {
            ArticleId = l.ArticleId,
            Reference = articles[l.ArticleId].Reference,
            Name = articles[l.ArticleId].Name,
            Quantity = l.Quantity,
            UnitPrice = Math.Round(l.UnitPrice, 3, MidpointRounding.AwayFromZero),
        }));
        order.Subtotal = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
        order.VatAmount = Math.Round(order.Subtotal * order.VatRate, 3, MidpointRounding.AwayFromZero);
        order.Total = order.Subtotal + order.VatAmount;
        if (ApplyPayment(order, request.PaymentStatus, request.AmountPaid) is { } invalid) return invalid;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (order.Id == 0) db.Orders.Add(order);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return null;
    }

    private async Task ReturnStockAsync(IEnumerable<OrderLine> lines)
    {
        foreach (var returned in lines.GroupBy(l => l.ArticleId))
        {
            var quantity = returned.Sum(l => l.Quantity);
            await db.Articles.Where(a => a.Id == returned.Key)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Quantity, a => a.Quantity + quantity));
        }
    }

    /// <summary>Paid means the whole total and Not paid means nothing, so the amount is only read for a partial payment.</summary>
    private ActionResult? ApplyPayment(Order order, PaymentStatus status, decimal amountPaid)
    {
        var amount = status switch
        {
            PaymentStatus.Paid => order.Total,
            PaymentStatus.PartiallyPaid => Math.Round(amountPaid, 3, MidpointRounding.AwayFromZero),
            _ => 0m,
        };
        if (status == PaymentStatus.PartiallyPaid && (amount <= 0 || amount >= order.Total))
        {
            ModelState.AddModelError(nameof(SavePaymentRequest.AmountPaid),
                $"A partial payment must be more than 0 and less than the total of {order.Total:0.000}.");
            return ValidationProblem(ModelState);
        }

        order.PaymentStatus = status;
        order.AmountPaid = amount;
        return null;
    }

    private async Task<OrderDto?> LoadDtoAsync(int id) =>
        await db.Orders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new OrderDto(
                o.Id,
                o.OrderDate,
                o.Customer == null ? null : new OrderCustomerDto(o.Customer.Id, o.Customer.Name, o.Customer.Address,
                    o.Customer.City, o.Customer.Phone, o.Customer.Email, o.Customer.TaxNumber),
                o.Notes,
                o.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(
                    l.Id, l.ArticleId, l.Reference, l.Name, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice)).ToList(),
                o.VatRate, o.Subtotal, o.VatAmount, o.Total, o.PaymentStatus, o.AmountPaid, o.Total - o.AmountPaid,
                o.CreatedAt, o.UpdatedAt))
            .SingleOrDefaultAsync();
}
