using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Data;
using UserAdmin.Api.Domain;

namespace UserAdmin.Api.Controllers;

[ApiController]
[Route("api/articles")]
public class ArticlesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.ArticlesRead)]
    public async Task<PagedResult<ArticleDto>> List(
        string? search, string? category, int? supplierId, bool? lowStock, bool? isActive,
        int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Articles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Reference.Contains(term) || a.Name.Contains(term)
                || a.Brand!.Contains(term) || a.Compatibility!.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(a => a.Category == category.Trim());
        if (supplierId is not null) query = query.Where(a => a.SupplierId == supplierId);
        if (lowStock is not null) query = query.Where(a => (a.Quantity <= a.MinimumStock) == lowStock);
        if (isActive is not null) query = query.Where(a => a.IsActive == isActive);

        var total = await query.CountAsync();
        var items = await ToDto(query.OrderBy(a => a.Name).ThenBy(a => a.Reference)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
        return new PagedResult<ArticleDto>(items, total, page, pageSize);
    }

    /// <summary>The categories in use, for filtering and for suggestions while typing one.</summary>
    [HttpGet("categories")]
    [Authorize(Permissions.ArticlesRead)]
    public async Task<IReadOnlyList<string>> Categories() =>
        await db.Articles.AsNoTracking().Where(a => a.Category != null)
            .Select(a => a.Category!).Distinct().OrderBy(c => c).ToListAsync();

    [HttpGet("stats")]
    [Authorize(Permissions.ArticlesRead)]
    public async Task<ArticleStatsDto> Stats()
    {
        var active = db.Articles.AsNoTracking().Where(a => a.IsActive);
        return new ArticleStatsDto(
            await active.CountAsync(),
            await active.CountAsync(a => a.Quantity <= a.MinimumStock),
            await active.CountAsync(a => a.Quantity == 0),
            await active.SumAsync(a => a.Quantity * a.PurchasePrice));
    }

    [HttpGet("{id:int}")]
    [Authorize(Permissions.ArticlesRead)]
    public async Task<ActionResult<ArticleDto>> Get(int id)
    {
        var article = await ToDto(db.Articles.AsNoTracking().Where(a => a.Id == id)).SingleOrDefaultAsync();
        return article is null ? NotFound() : article;
    }

    [HttpPost]
    [Authorize(Permissions.ArticlesWrite)]
    public async Task<ActionResult<ArticleDto>> Create(SaveArticleRequest request)
    {
        if (await db.Articles.AnyAsync(a => a.Reference == request.Reference.Trim())) return ReferenceTaken();
        if (await CheckSupplierAsync(request.SupplierId) is { } invalid) return invalid;

        var article = new Article { Reference = "", Name = "" };
        Apply(article, request);
        db.Articles.Add(article);
        await db.SaveChangesAsync();

        var dto = await ToDto(db.Articles.AsNoTracking().Where(a => a.Id == article.Id)).SingleAsync();
        return CreatedAtAction(nameof(Get), new { id = article.Id }, dto);
    }

    [HttpPut("{id:int}")]
    [Authorize(Permissions.ArticlesWrite)]
    public async Task<ActionResult<ArticleDto>> Update(int id, SaveArticleRequest request)
    {
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id);
        if (article is null) return NotFound();
        if (await db.Articles.AnyAsync(a => a.Reference == request.Reference.Trim() && a.Id != id)) return ReferenceTaken();
        if (request.SupplierId != article.SupplierId && await CheckSupplierAsync(request.SupplierId) is { } invalid)
            return invalid;

        Apply(article, request);
        await db.SaveChangesAsync();
        return await ToDto(db.Articles.AsNoTracking().Where(a => a.Id == id)).SingleAsync();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Permissions.ArticlesWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id);
        if (article is null) return NotFound();

        db.Articles.Remove(article);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult?> CheckSupplierAsync(int? supplierId)
    {
        if (supplierId is null || await db.Suppliers.AnyAsync(s => s.Id == supplierId)) return null;
        ModelState.AddModelError(nameof(SaveArticleRequest.SupplierId), "This supplier does not exist.");
        return ValidationProblem(ModelState);
    }

    private ConflictObjectResult ReferenceTaken() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Conflict",
            Detail = "An article with this reference already exists.",
        });

    private static void Apply(Article article, SaveArticleRequest request)
    {
        article.Reference = request.Reference.Trim();
        article.Name = request.Name.Trim();
        article.Brand = Input.Optional(request.Brand);
        article.Category = Input.Optional(request.Category);
        article.Compatibility = Input.Optional(request.Compatibility);
        article.Description = Input.Optional(request.Description);
        article.Location = Input.Optional(request.Location);
        article.SupplierId = request.SupplierId;
        article.PurchasePrice = request.PurchasePrice;
        article.SalePrice = request.SalePrice;
        article.Quantity = request.Quantity;
        article.MinimumStock = request.MinimumStock;
        article.IsActive = request.IsActive;
        article.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static IQueryable<ArticleDto> ToDto(IQueryable<Article> query) =>
        query.Select(a => new ArticleDto(
            a.Id, a.Reference, a.Name, a.Brand, a.Category, a.Compatibility, a.Description, a.Location,
            a.SupplierId, a.Supplier != null ? a.Supplier.Name : null, a.PurchasePrice, a.SalePrice,
            a.Quantity, a.MinimumStock, a.Quantity <= a.MinimumStock, a.IsActive, a.CreatedAt, a.UpdatedAt));
}
