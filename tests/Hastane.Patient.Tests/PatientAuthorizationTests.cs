using System.Net;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Patient.Tests;

// Yetki tablosu: her uç x her kimlik. 401 = kimlik yok/geçersiz, 403 = giriş var ama rol yanlış.
[Collection(PatientCollection.Name)]
public class PatientAuthorizationTests(PatientApiFactory factory) : PatientTestBase(factory)
{
    public static readonly TheoryData<string, string> PatientOnlyEndpoints = new()
    {
        { "GET", "/api/patients/me" },
        { "PUT", "/api/patients/me" },
        { "GET", "/api/patients/me/favorites" },
        { "PUT", "/api/patients/me/favorites/doc-1" },
        { "DELETE", "/api/patients/me/favorites/doc-1" },
    };

    public static readonly TheoryData<string, string> AllEndpoints = new()
    {
        { "GET", "/api/patients/me" },
        { "PUT", "/api/patients/me" },
        { "GET", "/api/patients/someone" },
        { "GET", "/api/patients/me/favorites" },
        { "PUT", "/api/patients/me/favorites/doc-1" },
        { "DELETE", "/api/patients/me/favorites/doc-1" },
    };

    private Task<HttpResponseMessage> Send(HttpClient client, string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT" && path == "/api/patients/me") request.Content = RawJson("{}");
        return client.SendAsync(request, Ct);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Anonymous_Returns401(string method, string path)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Send(ClientFor(), method, path)).StatusCode);

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task ExpiredToken_Returns401(string method, string path)
    {
        var token = TestJwt.CreateExpired(Unique("pat"), [HastaneRoles.Patient]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(ClientFor(token), method, path)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task BrokenSignature_Returns401(string method, string path)
    {
        var token = TestJwt.CreateWithBrokenSignature(Unique("pat"), [HastaneRoles.Patient]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(ClientFor(token), method, path)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(PatientOnlyEndpoints))]
    public async Task Doctor_OnPatientOnlyEndpoint_Returns403(string method, string path)
        => Assert.Equal(HttpStatusCode.Forbidden, (await Send(ClientFor(DoctorToken(Unique("doc"))), method, path)).StatusCode);

    [Theory]
    [MemberData(nameof(PatientOnlyEndpoints))]
    public async Task Admin_OnPatientOnlyEndpoint_Returns403(string method, string path)
        => Assert.Equal(HttpStatusCode.Forbidden, (await Send(ClientFor(AdminToken()), method, path)).StatusCode);

    [Theory]
    [MemberData(nameof(PatientOnlyEndpoints))]
    public async Task TokenWithoutRole_OnPatientOnlyEndpoint_Returns403(string method, string path)
    {
        var token = TestJwt.Create(Unique("usr"), []);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(ClientFor(token), method, path)).StatusCode);
    }

    [Fact]
    public async Task GetByUserId_TokenWithoutRole_Returns403()
    {
        var token = TestJwt.Create(Unique("usr"), []);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(token).GetAsync("/api/patients/someone", Ct)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(PatientOnlyEndpoints))]
    public async Task Patient_OnPatientOnlyEndpoint_IsAllowed(string method, string path)
    {
        FakeDownstream.RegisterDoctor("doc-1");
        var response = await Send(ClientFor(PatientToken(Unique("pat"))), method, path);

        Assert.True(response.IsSuccessStatusCode, $"{method} {path} -> {(int)response.StatusCode}");
    }

    [Fact]
    public async Task MeRoute_IsNotTreatedAsUserId_ForAdmin()
    {
        // "me" sabit rota; Admin için /api/patients/me 403 (Patient-only), {userId} rotasına düşmez.
        var response = await ClientFor(AdminToken()).GetAsync("/api/patients/me", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
