using System.Net;
using System.Net.Http.Json;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;

namespace UserAdmin.Tests;

[Collection("api")]
public class CatalogTests(ApiFixture api)
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static SaveContactRequest Contact(string name) =>
        new(name, "Sami Trabelsi", "Orders@Example.com", "+216 71 000 000", "12 rue de Marseille", "Tunis",
            "1234567A/M/000", null);

    private static SaveArticleRequest Article(string reference, int? supplierId = null, int quantity = 10, int minimum = 2) =>
        new(reference, "Brake pads front", "Bosch", "Brakes", "Peugeot 208 1.6 HDi", null, "A-12",
            supplierId, 45.5m, 79.9m, quantity, minimum);

    private async Task<int> CreateSupplierAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/suppliers", Contact(Unique("Supplier")))).ReadAsync<ContactDto>()).Id;

    [Fact]
    public async Task Articles_can_be_created_read_updated_and_deleted()
    {
        var admin = await api.AdminClientAsync();
        var supplierId = await CreateSupplierAsync(admin);
        var reference = Unique("REF");

        var created = await admin.PostAsJsonAsync("/api/articles", Article($"  {reference}  ", supplierId) with { Brand = " " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = await created.ReadAsync<ArticleDto>();
        Assert.Equal(reference, article.Reference);
        Assert.Null(article.Brand);
        Assert.Equal(45.5m, article.PurchasePrice);
        Assert.NotNull(article.SupplierName);
        Assert.False(article.IsLowStock);

        var updated = await (await admin.PutAsJsonAsync($"/api/articles/{article.Id}",
            Article(reference, null, quantity: 2, minimum: 2))).ReadAsync<ArticleDto>();
        Assert.Null(updated.SupplierId);
        Assert.True(updated.IsLowStock);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/articles/{article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/articles/{article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Article_references_are_unique_and_input_is_validated()
    {
        var admin = await api.AdminClientAsync();
        var reference = Unique("REF");
        (await admin.PostAsJsonAsync("/api/articles", Article(reference))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/articles", Article(reference))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/articles", Article(reference.ToLowerInvariant()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/articles", Article(Unique("REF"), quantity: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/articles", Article(Unique("REF")) with { SalePrice = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/articles", Article(Unique("REF"), supplierId: int.MaxValue))).StatusCode);
    }

    [Fact]
    public async Task Articles_can_be_searched_and_filtered()
    {
        var admin = await api.AdminClientAsync();
        var supplierId = await CreateSupplierAsync(admin);
        var marker = Guid.NewGuid().ToString("N")[..12];
        (await admin.PostAsJsonAsync("/api/articles",
            Article($"{marker}-1", supplierId, quantity: 0, minimum: 3) with { Compatibility = $"Clio {marker}" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/articles",
            Article($"{marker}-2", supplierId, quantity: 50, minimum: 3) with { Category = $"Filters {marker}" })).EnsureSuccessStatusCode();

        var bySearch = await admin.GetFromJsonAsync<PagedResult<ArticleDto>>($"/api/articles?search={marker}");
        Assert.Equal(2, bySearch!.Total);

        var low = await admin.GetFromJsonAsync<PagedResult<ArticleDto>>($"/api/articles?supplierId={supplierId}&lowStock=true");
        Assert.Equal($"{marker}-1", Assert.Single(low!.Items).Reference);

        var category = await admin.GetFromJsonAsync<PagedResult<ArticleDto>>(
            $"/api/articles?category={Uri.EscapeDataString($"Filters {marker}")}");
        Assert.Equal($"{marker}-2", Assert.Single(category!.Items).Reference);

        var categories = await admin.GetFromJsonAsync<List<string>>("/api/articles/categories");
        Assert.Contains($"Filters {marker}", categories!);

        var stats = await admin.GetFromJsonAsync<ArticleStatsDto>("/api/articles/stats");
        Assert.True(stats!.OutOfStock >= 1);
        Assert.True(stats.StockValue >= 50 * 45.5m);
    }

    [Fact]
    public async Task A_supplier_used_by_articles_cannot_be_deleted()
    {
        var admin = await api.AdminClientAsync();
        var supplierId = await CreateSupplierAsync(admin);
        var article = await (await admin.PostAsJsonAsync("/api/articles", Article(Unique("REF"), supplierId)))
            .ReadAsync<ArticleDto>();

        var refused = await admin.DeleteAsync($"/api/suppliers/{supplierId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 article", await refused.Content.ReadAsStringAsync());

        (await admin.DeleteAsync($"/api/articles/{article.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/suppliers/{supplierId}")).StatusCode);
    }

    [Theory]
    [InlineData("suppliers")]
    [InlineData("customers")]
    public async Task Contacts_can_be_managed_and_names_are_unique(string kind)
    {
        var admin = await api.AdminClientAsync();
        var name = Unique("Garage");

        var created = await admin.PostAsJsonAsync($"/api/{kind}", Contact(name) with { Notes = "  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var contact = await created.ReadAsync<ContactDto>();
        Assert.Equal("orders@example.com", contact.Email);
        Assert.Null(contact.Notes);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/{kind}", Contact(name))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/{kind}", Contact(Unique("Garage")) with { Email = "not-an-email" })).StatusCode);

        var updated = await (await admin.PutAsJsonAsync($"/api/{kind}/{contact.Id}",
            Contact(name) with { City = "Sfax", IsActive = false })).ReadAsync<ContactDto>();
        Assert.Equal("Sfax", updated.City);

        var inactive = await admin.GetFromJsonAsync<PagedResult<ContactDto>>(
            $"/api/{kind}?search={Uri.EscapeDataString(name)}&isActive=false");
        Assert.Equal(contact.Id, Assert.Single(inactive!.Items).Id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/{kind}/{contact.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/{kind}/{contact.Id}")).StatusCode);
    }

    [Fact]
    public async Task Each_module_needs_its_own_permissions()
    {
        var admin = await api.AdminClientAsync();
        var profileId = await AuthTests.CreateProfileAsync(admin, Permissions.ArticlesRead, Permissions.CustomersWrite);
        var (client, _, _) = await AuthTests.CreateSignedInUserAsync(api, admin, profileId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/articles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/suppliers/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/articles", Article(Unique("REF")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/suppliers")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/customers", Contact(Unique("Customer")))).StatusCode);

        var nothing = await AuthTests.CreateProfileAsync(admin);
        var (outsider, _, _) = await AuthTests.CreateSignedInUserAsync(api, admin, nothing);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/articles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Administrators_receive_the_new_permissions()
    {
        var admin = await api.AdminClientAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserDto>("/api/me");
        Assert.True(Permissions.All.IsSubsetOf(me!.Permissions));
        Assert.Contains(Permissions.ArticlesWrite, me.Permissions);
        Assert.Contains(Permissions.CustomersWrite, me.Permissions);
    }
}
