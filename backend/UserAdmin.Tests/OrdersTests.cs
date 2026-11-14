using System.Net;
using System.Net.Http.Json;
using UserAdmin.Api.Auth;
using UserAdmin.Api.Contracts;
using UserAdmin.Api.Domain;

namespace UserAdmin.Tests;

[Collection("api")]
public class OrdersTests(ApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static async Task<ArticleDto> CreateArticleAsync(HttpClient admin, int quantity, decimal salePrice = 100m) =>
        await (await admin.PostAsJsonAsync("/api/articles", new SaveArticleRequest(
            Unique("REF"), "Oil filter", "Mann", "Filters", null, null, null, null, 20m, salePrice, quantity, 0)))
            .ReadAsync<ArticleDto>();

    private static async Task<int> CreateCustomerAsync(HttpClient admin) =>
        (await (await admin.PostAsJsonAsync("/api/customers", new SaveContactRequest(
            Unique("Garage"), null, null, null, "5 avenue Habib Bourguiba", "Sousse", "7654321B/A/M/000", null)))
            .ReadAsync<ContactDto>()).Id;

    private static OrderLineRequest Line(int articleId, int quantity, decimal unitPrice) => new(articleId, quantity, unitPrice);

    private static SaveOrderRequest Order(int? customerId, params OrderLineRequest[] lines) =>
        new(Today, customerId, null, lines, PaymentStatus.NotPaid, 0);

    private static async Task<int> StockOfAsync(HttpClient admin, int articleId) =>
        (await admin.GetFromJsonAsync<ArticleDto>($"/api/articles/{articleId}"))!.Quantity;

    [Fact]
    public async Task Creating_an_order_takes_stock_and_adds_vat()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);
        var customerId = await CreateCustomerAsync(admin);

        var created = await admin.PostAsJsonAsync("/api/orders",
            Order(customerId, Line(article.Id, 3, 12.5m), Line(article.Id, 1, 10.1234m)) with { Notes = "  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.ReadAsync<OrderDto>();

        Assert.Equal(6, await StockOfAsync(admin, article.Id));
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(article.Reference, order.Lines[0].Reference);
        Assert.Equal(10.123m, order.Lines[1].UnitPrice);
        Assert.Equal(47.623m, order.Subtotal);
        Assert.Equal(0.19m, order.VatRate);
        Assert.Equal(9.048m, order.VatAmount);
        Assert.Equal(56.671m, order.Total);
        Assert.Equal(56.671m, order.AmountDue);
        Assert.Equal("Sousse", order.Customer!.City);
        Assert.Null(order.Notes);
    }

    [Fact]
    public async Task Editing_an_order_moves_stock_and_deleting_it_returns_stock()
    {
        var admin = await api.AdminClientAsync();
        var first = await CreateArticleAsync(admin, quantity: 5);
        var second = await CreateArticleAsync(admin, quantity: 5);
        var order = await (await admin.PostAsJsonAsync("/api/orders", Order(null, Line(first.Id, 5, 10m))))
            .ReadAsync<OrderDto>();
        Assert.Equal(0, await StockOfAsync(admin, first.Id));

        // The order's own 5 units count as available when it is edited.
        var edited = await admin.PutAsJsonAsync($"/api/orders/{order.Id}",
            Order(null, Line(first.Id, 2, 10m), Line(second.Id, 4, 10m)));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(3, await StockOfAsync(admin, first.Id));
        Assert.Equal(1, await StockOfAsync(admin, second.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(5, await StockOfAsync(admin, first.Id));
        Assert.Equal(5, await StockOfAsync(admin, second.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_order_cannot_take_more_than_is_in_stock()
    {
        var admin = await api.AdminClientAsync();
        var plenty = await CreateArticleAsync(admin, quantity: 10);
        var scarce = await CreateArticleAsync(admin, quantity: 2);

        var refused = await admin.PostAsJsonAsync("/api/orders",
            Order(null, Line(plenty.Id, 4, 10m), Line(scarce.Id, 2, 10m), Line(scarce.Id, 1, 10m)));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains($"Only 2 of {scarce.Reference} are in stock", await refused.Content.ReadAsStringAsync());

        // Nothing was taken from either article.
        Assert.Equal(10, await StockOfAsync(admin, plenty.Id));
        Assert.Equal(2, await StockOfAsync(admin, scarce.Id));

        var order = await (await admin.PostAsJsonAsync("/api/orders", Order(null, Line(scarce.Id, 2, 10m))))
            .ReadAsync<OrderDto>();
        var editRefused = await admin.PutAsJsonAsync($"/api/orders/{order.Id}", Order(null, Line(scarce.Id, 3, 10m)));
        Assert.Equal(HttpStatusCode.Conflict, editRefused.StatusCode);
        Assert.Equal(0, await StockOfAsync(admin, scarce.Id));
        Assert.Equal(2, (await admin.GetFromJsonAsync<OrderDto>($"/api/orders/{order.Id}"))!.Lines.Single().Quantity);
    }

    [Fact]
    public async Task Orders_are_validated()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);

        async Task<HttpStatusCode> Post(SaveOrderRequest request) =>
            (await admin.PostAsJsonAsync("/api/orders", request)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(null)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(null, Line(article.Id, 0, 10m))));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(null, Line(article.Id, 1, -1m))));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(null, Line(int.MaxValue, 1, 10m))));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(int.MaxValue, Line(article.Id, 1, 10m))));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Order(null, Line(article.Id, 1, 10m)) with { OrderDate = default }));

        var inactive = await CreateArticleAsync(admin, quantity: 10);
        (await admin.PutAsJsonAsync($"/api/articles/{inactive.Id}", new SaveArticleRequest(inactive.Reference, inactive.Name,
            null, null, null, null, null, null, 20m, 100m, 10, 0, IsActive: false))).EnsureSuccessStatusCode();
        var refused = await admin.PostAsJsonAsync("/api/orders", Order(null, Line(inactive.Id, 1, 10m)));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("inactive", await refused.Content.ReadAsStringAsync());
        Assert.Equal(10, await StockOfAsync(admin, article.Id));
    }

    [Fact]
    public async Task Payments_follow_the_status()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);
        var order = await (await admin.PostAsJsonAsync("/api/orders",
            Order(null, Line(article.Id, 1, 100m)) with { PaymentStatus = PaymentStatus.Paid })).ReadAsync<OrderDto>();
        Assert.Equal(119m, order.AmountPaid);
        Assert.Equal(0m, order.AmountDue);

        var partial = await (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payment",
            new SavePaymentRequest(PaymentStatus.PartiallyPaid, 50.0004m))).ReadAsync<OrderDto>();
        Assert.Equal(PaymentStatus.PartiallyPaid, partial.PaymentStatus);
        Assert.Equal(50m, partial.AmountPaid);
        Assert.Equal(69m, partial.AmountDue);

        foreach (var amount in new[] { 0m, 119m, 200m })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payment",
                new SavePaymentRequest(PaymentStatus.PartiallyPaid, amount))).StatusCode);
        }

        // An amount sent with Not paid is ignored.
        var unpaid = await (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payment",
            new SavePaymentRequest(PaymentStatus.NotPaid, 30m))).ReadAsync<OrderDto>();
        Assert.Equal(0m, unpaid.AmountPaid);

        // A partial payment that would cover a smaller new total is refused, and the order is left as it was.
        (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/payment",
            new SavePaymentRequest(PaymentStatus.PartiallyPaid, 100m))).EnsureSuccessStatusCode();
        var shrink = await admin.PutAsJsonAsync($"/api/orders/{order.Id}",
            Order(null, Line(article.Id, 1, 50m)) with { PaymentStatus = PaymentStatus.PartiallyPaid, AmountPaid = 100m });
        Assert.Equal(HttpStatusCode.BadRequest, shrink.StatusCode);
        Assert.Equal(119m, (await admin.GetFromJsonAsync<OrderDto>($"/api/orders/{order.Id}"))!.Total);
        Assert.Equal(9, await StockOfAsync(admin, article.Id));
    }

    [Fact]
    public async Task Orders_can_be_listed_and_filtered()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);
        var customerId = await CreateCustomerAsync(admin);
        var paid = await (await admin.PostAsJsonAsync("/api/orders",
            Order(customerId, Line(article.Id, 1, 100m)) with { PaymentStatus = PaymentStatus.Paid })).ReadAsync<OrderDto>();
        var unpaid = await (await admin.PostAsJsonAsync("/api/orders", Order(customerId, Line(article.Id, 2, 100m))))
            .ReadAsync<OrderDto>();

        var forCustomer = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>($"/api/orders?customerId={customerId}");
        Assert.Equal([unpaid.Id, paid.Id], forCustomer!.Items.Select(o => o.Id));
        Assert.Equal(238m, forCustomer.Items[0].AmountDue);

        var notPaid = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>(
            $"/api/orders?customerId={customerId}&paymentStatus=NotPaid");
        Assert.Equal(unpaid.Id, Assert.Single(notPaid!.Items).Id);

        var byNumber = await admin.GetFromJsonAsync<PagedResult<OrderSummaryDto>>($"/api/orders?search=ORD-{paid.Id:D5}");
        Assert.Contains(byNumber!.Items, o => o.Id == paid.Id);

        var stats = await admin.GetFromJsonAsync<OrderStatsDto>("/api/orders/stats");
        Assert.True(stats!.Outstanding >= 1);
        Assert.True(stats.AmountDue >= 238m);
    }

    [Fact]
    public async Task Articles_and_customers_on_orders_cannot_be_deleted()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);
        var customerId = await CreateCustomerAsync(admin);
        var order = await (await admin.PostAsJsonAsync("/api/orders", Order(customerId, Line(article.Id, 1, 10m))))
            .ReadAsync<OrderDto>();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/articles/{article.Id}")).StatusCode);
        var customerRefused = await admin.DeleteAsync($"/api/customers/{customerId}");
        Assert.Equal(HttpStatusCode.Conflict, customerRefused.StatusCode);
        Assert.Contains("1 order", await customerRefused.Content.ReadAsStringAsync());

        (await admin.DeleteAsync($"/api/orders/{order.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/articles/{article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/customers/{customerId}")).StatusCode);
    }

    [Fact]
    public async Task Orders_need_their_own_permissions()
    {
        var admin = await api.AdminClientAsync();
        var article = await CreateArticleAsync(admin, quantity: 10);

        var writerProfile = await AuthTests.CreateProfileAsync(admin, Permissions.OrdersRead, Permissions.OrdersWrite);
        var (writer, _, _) = await AuthTests.CreateSignedInUserAsync(api, admin, writerProfile);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync($"/api/articles/options?search={article.Reference}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync("/api/customers/options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetAsync("/api/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync("/api/articles")).StatusCode);
        var created = await writer.PostAsJsonAsync("/api/orders", Order(null, Line(article.Id, 1, 10m)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var orderId = (await created.ReadAsync<OrderDto>()).Id;

        var readerProfile = await AuthTests.CreateProfileAsync(admin, Permissions.OrdersRead);
        var (reader, _, _) = await AuthTests.CreateSignedInUserAsync(api, admin, readerProfile);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/articles/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"/api/orders/{orderId}/payment",
            new SavePaymentRequest(PaymentStatus.Paid, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"/api/orders/{orderId}")).StatusCode);

        var nothing = await AuthTests.CreateProfileAsync(admin);
        var (outsider, _, _) = await AuthTests.CreateSignedInUserAsync(api, admin, nothing);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/company")).StatusCode);
    }
}
