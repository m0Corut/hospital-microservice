using System.Net;
using System.Net.Http.Json;
using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Hastane.Patient.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Patient.Tests;

[Collection(PatientCollection.Name)]
public class FavoritesApiTests(PatientApiFactory factory) : PatientTestBase(factory)
{
    private static string NewDoctor()
    {
        var id = Unique("doc");
        FakeDownstream.RegisterDoctor(id);
        return id;
    }

    private static Task<HttpResponseMessage> Add(HttpClient c, string doctorId) => c.PutAsync($"/api/patients/me/favorites/{doctorId}", null, Ct);

    private static async Task<List<FavoriteDoctorDto>> List(HttpClient c)
    {
        var response = await c.GetAsync("/api/patients/me/favorites", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<List<FavoriteDoctorDto>>(response);
    }

    [Fact]
    public async Task List_WhenEmpty_ReturnsEmptyArray()
    {
        Assert.Empty(await List(ClientFor(PatientToken(Unique("pat")))));
    }

    [Fact]
    public async Task Add_Then_List_ContainsDoctorWithCreatedAt()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();

        var put = await Add(client, doctor);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var item = Assert.Single(await List(client));
        Assert.Equal(doctor, item.DoctorId);
        Assert.True((DateTime.UtcNow - item.CreatedAt).TotalMinutes < 5);
    }

    [Fact]
    public async Task Add_Twice_IsIdempotent_SingleRow()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));
        var doctor = NewDoctor();

        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, doctor)).StatusCode);

        Assert.Single(await List(client));
        Assert.Equal(1, await WithDbAsync(db => db.Favorites.CountAsync(f => f.UserId == sub, Ct)));
    }

    [Fact]
    public async Task Add_Again_DoesNotCallDoctorService()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();
        await Add(client, doctor);
        var before = FakeDownstream.CallCount($"/api/doctors/{doctor}");

        await Add(client, doctor);

        Assert.Equal(before, FakeDownstream.CallCount($"/api/doctors/{doctor}"));
    }

    [Fact]
    public async Task Add_ChecksDoctorViaPublicEndpoint_ForwardingAuthorization()
    {
        var token = PatientToken(Unique("pat"));
        var doctor = NewDoctor();

        await Add(ClientFor(token), doctor);

        Assert.Contains(FakeDownstream.Calls, c => c.PathAndQuery == $"/api/doctors/{doctor}/public" && c.Authorization == "Bearer " + token);
    }

    [Fact]
    public async Task Add_UnknownDoctor_Returns404_AndNothingIsStored()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));

        var response = await Add(client, Unique("ghost"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Add_DoctorIdTooLong_Returns400()
    {
        var response = await Add(ClientFor(PatientToken(Unique("pat"))), new string('d', 65));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_DoctorIdOfExactLimit_IsAccepted()
    {
        var id = new string('d', 64);
        FakeDownstream.RegisterDoctor(id);

        var response = await Add(ClientFor(PatientToken(Unique("pat"))), id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Favorites_AreIsolatedPerPatient()
    {
        var a = ClientFor(PatientToken(Unique("pat")));
        var b = ClientFor(PatientToken(Unique("pat")));
        await Add(a, NewDoctor());

        Assert.Single(await List(a));
        Assert.Empty(await List(b));
    }

    [Fact]
    public async Task Remove_RemovesFavorite_AndIsIdempotent()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();
        await Add(client, doctor);

        var first = await client.DeleteAsync($"/api/patients/me/favorites/{doctor}", Ct);
        var second = await client.DeleteAsync($"/api/patients/me/favorites/{doctor}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Remove_NeverFavorited_Returns204()
    {
        var response = await ClientFor(PatientToken(Unique("pat"))).DeleteAsync($"/api/patients/me/favorites/{Unique("doc")}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Remove_DoesNotTouchOtherPatientsFavorite()
    {
        var other = Unique("pat");
        var doctor = NewDoctor();
        await Add(ClientFor(PatientToken(other)), doctor);

        await ClientFor(PatientToken(Unique("pat"))).DeleteAsync($"/api/patients/me/favorites/{doctor}", Ct);

        Assert.Single(await List(ClientFor(PatientToken(other))));
    }

    [Fact]
    public async Task Remove_WorksEvenWhenDoctorServiceIsDown()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();
        await Add(client, doctor);
        FakeDownstream.DoctorMode = DownstreamMode.Down;

        var response = await client.DeleteAsync($"/api/patients/me/favorites/{doctor}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- 100 sınırı ----

    private async Task SeedFavoritesAsync(string sub, int count)
        => await WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
                db.Favorites.Add(new FavoriteDoctor { UserId = sub, DoctorId = $"seed-{i:D3}", CreatedAt = DateTime.UtcNow });
            return await db.SaveChangesAsync(Ct);
        });

    [Fact]
    public async Task Add_AtLimit_Returns409_AndExistingOnesStillIdempotent()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));
        await SeedFavoritesAsync(sub, PatientService.MaxFavorites);

        var overLimit = await Add(client, NewDoctor());
        var existing = await Add(client, "seed-000");

        Assert.Equal(HttpStatusCode.Conflict, overLimit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, existing.StatusCode);
        Assert.Equal(PatientService.MaxFavorites, (await List(client)).Count);
    }

    [Fact]
    public async Task Add_JustBelowLimit_IsAccepted_ThenLimitApplies()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));
        await SeedFavoritesAsync(sub, PatientService.MaxFavorites - 1);

        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, NewDoctor())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Add(client, NewDoctor())).StatusCode);
    }

    [Fact]
    public async Task Add_AfterRemovingOne_AtLimit_IsAccepted()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));
        await SeedFavoritesAsync(sub, PatientService.MaxFavorites);
        await client.DeleteAsync("/api/patients/me/favorites/seed-000", Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, NewDoctor())).StatusCode);
    }

    [Fact]
    public async Task Limit_IsPerPatient()
    {
        var full = Unique("pat");
        await SeedFavoritesAsync(full, PatientService.MaxFavorites);

        Assert.Equal(HttpStatusCode.NoContent, (await Add(ClientFor(PatientToken(Unique("pat"))), NewDoctor())).StatusCode);
    }

    // ---- Doctor servisi kapalı ----

    [Theory]
    [InlineData(DownstreamMode.Down)]
    [InlineData(DownstreamMode.Timeout)]
    [InlineData(DownstreamMode.Error500)]
    public async Task Add_WhenDoctorServiceIsUnavailable_Returns503_AndNothingIsStored(DownstreamMode mode)
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();
        FakeDownstream.DoctorMode = mode;

        var response = await Add(client, doctor);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        FakeDownstream.Reset();
        Assert.Empty(await List(client));
    }

    [Fact]
    public async Task Add_AlreadyFavorite_EvenIfDoctorServiceIsDown_Returns204()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var doctor = NewDoctor();
        await Add(client, doctor);
        FakeDownstream.DoctorMode = DownstreamMode.Down;

        Assert.Equal(HttpStatusCode.NoContent, (await Add(client, doctor)).StatusCode);
    }

    [Fact]
    public async Task List_IsNewestFirst()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var first = NewDoctor();
        var second = NewDoctor();
        await Add(client, first);
        await Task.Delay(30, Ct);
        await Add(client, second);

        var items = await List(client);

        Assert.Equal([second, first], items.Select(i => i.DoctorId).ToArray());
    }
}
