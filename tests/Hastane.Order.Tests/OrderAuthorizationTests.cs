using System.Net;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Order.Tests;

// JWT-TASARIM.md §5/§7: kimlik ve rol kuralları.
[Collection(OrderCollection.Name)]
public class OrderAuthorizationTests : OrderTestBase
{
    public OrderAuthorizationTests(OrderApiFactory factory) : base(factory)
    {
    }

    private const string SomeId = "00000000-0000-0000-0000-000000000001";

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "POST", "/api/orders/checkout" },
        { "GET", "/api/orders" },
        { "GET", "/api/orders/" + SomeId },
        { "PATCH", "/api/orders/" + SomeId + "/status" },
        { "POST", "/api/orders/" + SomeId + "/cancel" },
    };

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().SendAsync(Req(method, url), Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task InvalidTokens_Return401(string method, string url)
    {
        var tokens = new[]
        {
            TestJwt.CreateExpired("u1", [HastaneRoles.Admin]),
            TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]),
            TestJwt.CreateAlgNone("u1", [HastaneRoles.Admin]),
            TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin]),
        };
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Doctor_Gets403_Everywhere(string method, string url)
        => Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(DoctorToken()).SendAsync(Req(method, url), Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task UserWithoutRole_Gets403_Everywhere(string method, string url)
    {
        var token = TestJwt.Create(Unique("usr"), roles: []);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotCheckout_OnlyPatients()
    {
        var response = await CheckoutAsync(ClientFor(AdminToken()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patient_CannotChangeStatus_Returns403()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);

        var response = await PatchStatusAsync(ClientFor(PatientToken(sub)), order.Id, "Preparing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InternalMarketKey_IsNeverExposedInResponses()
    {
        var sub = Unique("pat");
        var order = await PlaceOrderAsync(sub);

        var body = await (await ClientFor(PatientToken(sub)).GetAsync($"/api/orders/{order.Id}", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain(FakeMarket.InternalKey, body);
    }
}
