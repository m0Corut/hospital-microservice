using System.Net;
using System.Net.Http.Json;
using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Patient.Tests;

[Collection(PatientCollection.Name)]
public class ProfileApiTests(PatientApiFactory factory) : PatientTestBase(factory)
{
    [Fact]
    public async Task GetMe_WithoutRecord_Returns200WithEmptyProfile()
    {
        var sub = Unique("pat");
        var response = await ClientFor(PatientToken(sub)).GetAsync("/api/patients/me", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Equal(sub, profile.UserId);
        Assert.Null(profile.DateOfBirth);
        Assert.Null(profile.Age);
        Assert.Null(profile.Gender);
        Assert.Null(profile.BloodType);
        Assert.Null(profile.BloodTypeDisplay);
        Assert.Null(profile.Phone);
        Assert.Null(profile.UpdatedAt);
        // GET kayıt YARATMAZ.
        Assert.Equal(0, await WithDbAsync(db => db.Profiles.CountAsync(p => p.UserId == sub, Ct)));
    }

    [Fact]
    public async Task PutMe_CreatesProfile_ThenGetReturnsIt()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));

        var put = await client.PutAsJsonAsync("/api/patients/me", FullProfile(), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var profile = await ReadAsync<PatientProfileDto>(await client.GetAsync("/api/patients/me", Ct));
        Assert.Equal(sub, profile.UserId);
        Assert.Equal(new DateOnly(1990, 5, 17), profile.DateOfBirth);
        Assert.Equal(Gender.Female, profile.Gender);
        Assert.Equal(BloodType.APos, profile.BloodType);
        Assert.Equal("A Rh+", profile.BloodTypeDisplay);
        Assert.Equal("+90 532 111 22 33", profile.Phone);
        Assert.Equal("Ankara", profile.City);
        Assert.Equal("Ayşe Yılmaz", profile.EmergencyContactName);
        Assert.NotNull(profile.UpdatedAt);
        Assert.True((DateTime.UtcNow - profile.UpdatedAt!.Value).TotalMinutes < 5);
    }

    [Fact]
    public async Task PutMe_JsonUsesTextEnumsAndDisplayName()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var response = await client.PutAsync("/api/patients/me", RawJson("""{"gender":"Other","bloodType":"ZeroNeg"}"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"gender\":\"Other\"", body);
        Assert.Contains("\"bloodType\":\"ZeroNeg\"", body);
        Assert.Contains("\"bloodTypeDisplay\":\"0 Rh-\"", body);
    }

    [Fact]
    public async Task PutMe_Twice_UpdatesSameRow_AndClearsOmittedFields()
    {
        var sub = Unique("pat");
        var client = ClientFor(PatientToken(sub));
        await client.PutAsJsonAsync("/api/patients/me", FullProfile(), Json, Ct);

        var second = await client.PutAsync("/api/patients/me", RawJson("""{"city":"İzmir"}"""), Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var profile = await ReadAsync<PatientProfileDto>(await client.GetAsync("/api/patients/me", Ct));
        Assert.Equal("İzmir", profile.City);
        Assert.Null(profile.Phone); // PUT tam değiştirme: gönderilmeyen alan boşalır
        Assert.Null(profile.DateOfBirth);
        Assert.Equal(1, await WithDbAsync(db => db.Profiles.CountAsync(p => p.UserId == sub, Ct)));
    }

    [Fact]
    public async Task PutMe_BlankStrings_StoredAsNull_AndTrimmed()
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var response = await client.PutAsync("/api/patients/me", RawJson("""{"address":"   ","city":"  Bursa  "}"""), Ct);

        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Null(profile.Address);
        Assert.Equal("Bursa", profile.City);
    }

    [Fact]
    public async Task Profiles_AreIsolatedPerPatient()
    {
        var a = ClientFor(PatientToken(Unique("pat")));
        var b = ClientFor(PatientToken(Unique("pat")));
        await a.PutAsJsonAsync("/api/patients/me", FullProfile(), Json, Ct);

        var bProfile = await ReadAsync<PatientProfileDto>(await b.GetAsync("/api/patients/me", Ct));
        Assert.Null(bProfile.City);
    }

    [Fact]
    public async Task PutMe_IgnoresUserIdAndUpdatedAtFromBody_MassAssignment()
    {
        var sub = Unique("pat");
        var victim = Unique("victim");
        var client = ClientFor(PatientToken(sub));

        var response = await client.PutAsync("/api/patients/me", RawJson(
            $$"""{"userId":"{{victim}}","updatedAt":"2000-01-01T00:00:00Z","age":99,"bloodTypeDisplay":"HACK","city":"Ankara"}"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Equal(sub, profile.UserId);
        Assert.Null(profile.Age);
        Assert.Null(profile.BloodTypeDisplay);
        Assert.True(profile.UpdatedAt!.Value.Year >= 2026);

        Assert.Equal(0, await WithDbAsync(db => db.Profiles.CountAsync(p => p.UserId == victim, Ct)));
        Assert.Equal(1, await WithDbAsync(db => db.Profiles.CountAsync(p => p.UserId == sub, Ct)));
    }

    [Theory]
    [InlineData("APos", "A Rh+")]
    [InlineData("ANeg", "A Rh-")]
    [InlineData("BPos", "B Rh+")]
    [InlineData("BNeg", "B Rh-")]
    [InlineData("ABPos", "AB Rh+")]
    [InlineData("ABNeg", "AB Rh-")]
    [InlineData("ZeroPos", "0 Rh+")]
    [InlineData("ZeroNeg", "0 Rh-")]
    public async Task BloodType_IsShownInRhFormat(string code, string display)
    {
        var client = ClientFor(PatientToken(Unique("pat")));
        var response = await client.PutAsync("/api/patients/me", RawJson($$"""{"bloodType":"{{code}}"}"""), Ct);

        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Equal(display, profile.BloodTypeDisplay);
    }

    [Fact]
    public async Task Age_IsComputedFromDateOfBirth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var client = ClientFor(PatientToken(Unique("pat")));

        // Doğum günü dün: 30 yaşını dün doldurdu.
        var passed = today.AddYears(-30).AddDays(-1);
        var p1 = await ReadAsync<PatientProfileDto>(await client.PutAsync("/api/patients/me", RawJson($$"""{"dateOfBirth":"{{passed:yyyy-MM-dd}}"}"""), Ct));
        Assert.Equal(30, p1.Age);

        // Doğum günü yarın: henüz 29.
        var upcoming = today.AddYears(-30).AddDays(1);
        var p2 = await ReadAsync<PatientProfileDto>(await client.PutAsync("/api/patients/me", RawJson($$"""{"dateOfBirth":"{{upcoming:yyyy-MM-dd}}"}"""), Ct));
        Assert.Equal(29, p2.Age);

        // GET da yaşı hesaplar.
        var got = await ReadAsync<PatientProfileDto>(await client.GetAsync("/api/patients/me", Ct));
        Assert.Equal(29, got.Age);
    }

    [Fact]
    public async Task Age_IsNotStoredInDatabase()
    {
        var sub = Unique("pat");
        await ClientFor(PatientToken(sub)).PutAsJsonAsync("/api/patients/me", FullProfile(), Json, Ct);

        var columns = await WithDbAsync(db => Task.FromResult(db.Model.FindEntityType(typeof(PatientProfile))!
            .GetProperties().Select(p => p.Name).ToList()));
        Assert.DoesNotContain("Age", columns);
    }

    // ---- Doğrulama ----

    [Fact]
    public async Task PutMe_FutureBirthDate_Returns400()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson($$"""{"dateOfBirth":"{{future:yyyy-MM-dd}}"}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutMe_BirthDateBefore1900_Returns400()
    {
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson("""{"dateOfBirth":"1899-12-31"}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutMe_BirthDate1900_IsAccepted()
    {
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson("""{"dateOfBirth":"1900-01-01"}"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("phone", 21)]
    [InlineData("address", 301)]
    [InlineData("city", 61)]
    [InlineData("emergencyContactName", 101)]
    [InlineData("emergencyContactPhone", 21)]
    public async Task PutMe_TooLongField_Returns400(string field, int length)
    {
        // Telefon alanları için de uzunluk (desen değil) sınırını sınamak üzere rakamlardan oluşan değer.
        var value = new string('1', length);
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson($$"""{"{{field}}":"{{value}}"}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("phone", 20)]
    [InlineData("address", 300)]
    [InlineData("city", 60)]
    [InlineData("emergencyContactName", 100)]
    [InlineData("emergencyContactPhone", 20)]
    public async Task PutMe_FieldAtExactLimit_IsAccepted(string field, int length)
    {
        var value = new string('1', length);
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson($$"""{"{{field}}":"{{value}}"}"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("<script>12345</script>")]
    [InlineData("0532-111-22-33; DROP")]
    public async Task PutMe_InvalidPhonePattern_Returns400(string phone)
    {
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PutAsync("/api/patients/me", RawJson($$"""{"phone":"{{phone}}","emergencyContactPhone":"{{phone}}"}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"gender":"Robot"}""")]
    [InlineData("""{"gender":1}""")]
    [InlineData("""{"bloodType":"XPos"}""")]
    [InlineData("""{"bloodType":99}""")]
    [InlineData("""{"dateOfBirth":"yarin"}""")]
    public async Task PutMe_InvalidEnumOrDate_Returns400(string body)
    {
        var response = await ClientFor(PatientToken(Unique("pat"))).PutAsync("/api/patients/me", RawJson(body), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutMe_InvalidBody_DoesNotCreateRow()
    {
        var sub = Unique("pat");
        await ClientFor(PatientToken(sub)).PutAsync("/api/patients/me", RawJson("""{"city":"x","dateOfBirth":"1850-01-01"}"""), Ct);

        Assert.Equal(0, await WithDbAsync(db => db.Profiles.CountAsync(p => p.UserId == sub, Ct)));
    }
}
