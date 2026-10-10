using System.Net;
using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;

namespace Hastane.Order.Tests;

// Sahiplik ve listeleme: hasta yalnız kendi siparişini görür (başkasınınki 404: varlığı bile sızmaz); Admin hepsini.
[Collection(OrderCollection.Name)]
public class OrderAccessTests : OrderTestBase
{
    public OrderAccessTests(OrderApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Owner_CanReadOwnOrder()
    {
        var sub = Unique("pat");
        var placed = await PlaceOrderAsync(sub);

        var response = await ClientFor(PatientToken(sub)).GetAsync($"/api/orders/{placed.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(placed.OrderNumber, (await ReadAsync<GetByIdOrderDto>(response)).OrderNumber);
    }

    [Fact]
    public async Task AnotherPatient_GetsNotFound_ForReadAndCancel()
    {
        var placed = await PlaceOrderAsync(Unique("pat"));
        var stranger = ClientFor(PatientToken(Unique("pat")));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/orders/{placed.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/orders/{placed.Id}/cancel", Raw("{}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadAnyOrder()
    {
        var placed = await PlaceOrderAsync(Unique("pat"));

        var response = await ClientFor(AdminToken()).GetAsync($"/api/orders/{placed.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOrder_Returns404()
    {
        var response = await ClientFor(AdminToken()).GetAsync($"/api/orders/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonGuidId_Returns404_NotServerError()
    {
        var response = await ClientFor(AdminToken()).GetAsync("/api/orders/abc", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatientList_ContainsOnlyOwnOrders_NewestFirst()
    {
        var mine = Unique("pat");
        var first = await PlaceOrderAsync(mine);
        var second = await PlaceOrderAsync(mine);
        await PlaceOrderAsync(Unique("pat")); // başkasının siparişi

        var response = await ClientFor(PatientToken(mine)).GetAsync("/api/orders", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadAsync<PagedResult<ResultOrderDto>>(response);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal([second.Id, first.Id], page.Items.Select(i => i.Id));
        Assert.All(page.Items, i => Assert.Equal(mine, i.PatientUserId));
        Assert.All(page.Items, i => Assert.Equal(1, i.ItemCount));
    }

    [Fact]
    public async Task AdminList_SeesEveryone_AndFiltersByStatus()
    {
        var admin = ClientFor(AdminToken());
        var a = await PlaceOrderAsync(Unique("pat"));
        var b = await PlaceOrderAsync(Unique("pat"));
        Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(admin, b.Id, "Preparing")).StatusCode);

        var all = await ReadAsync<PagedResult<ResultOrderDto>>(await admin.GetAsync("/api/orders?pageSize=50", Ct));
        var preparing = await ReadAsync<PagedResult<ResultOrderDto>>(await admin.GetAsync("/api/orders?status=Preparing&pageSize=50", Ct));

        Assert.Contains(all.Items, i => i.Id == a.Id);
        Assert.Contains(all.Items, i => i.Id == b.Id);
        Assert.Contains(preparing.Items, i => i.Id == b.Id);
        Assert.DoesNotContain(preparing.Items, i => i.Id == a.Id);
        Assert.All(preparing.Items, i => Assert.Equal(OrderStatus.Preparing, i.Status));
    }

    [Fact]
    public async Task List_IsPaged()
    {
        var sub = Unique("pat");
        for (var i = 0; i < 3; i++) await PlaceOrderAsync(sub);
        var client = ClientFor(PatientToken(sub));

        var page1 = await ReadAsync<PagedResult<ResultOrderDto>>(await client.GetAsync("/api/orders?page=1&pageSize=2", Ct));
        var page2 = await ReadAsync<PagedResult<ResultOrderDto>>(await client.GetAsync("/api/orders?page=2&pageSize=2", Ct));

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=500")]
    [InlineData("status=Nonsense")]
    public async Task InvalidListQuery_Returns400(string query)
    {
        var response = await ClientFor(AdminToken()).GetAsync("/api/orders?" + query, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
