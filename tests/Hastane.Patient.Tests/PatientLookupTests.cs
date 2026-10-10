using System.Net;
using System.Net.Http.Json;
using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Patient.Tests;

// GET /api/patients/{userId}: Admin hepsi, Doctor yalnız kendisiyle randevusu olan hasta, Patient yalnız kendisi.
[Collection(PatientCollection.Name)]
public class PatientLookupTests(PatientApiFactory factory) : PatientTestBase(factory)
{
    private async Task<string> CreatePatientWithProfileAsync()
    {
        var sub = Unique("pat");
        var put = await ClientFor(PatientToken(sub)).PutAsJsonAsync("/api/patients/me", FullProfile(), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        return sub;
    }

    [Fact]
    public async Task Admin_CanSeeAnyProfile_WithoutCallingAppointment()
    {
        var sub = await CreatePatientWithProfileAsync();
        var before = FakeDownstream.CallCount("/api/appointments");

        var response = await ClientFor(AdminToken()).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Equal(sub, profile.UserId);
        Assert.Equal("Ankara", profile.City);
        Assert.Equal(before, FakeDownstream.CallCount("/api/appointments"));
    }

    [Fact]
    public async Task Admin_UnknownPatient_Returns404()
    {
        var response = await ClientFor(AdminToken()).GetAsync($"/api/patients/{Unique("nobody")}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patient_CanSeeOwnProfileById()
    {
        var sub = await CreatePatientWithProfileAsync();

        var response = await ClientFor(PatientToken(sub)).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ankara", (await ReadAsync<PatientProfileDto>(response)).City);
    }

    [Fact]
    public async Task Patient_CannotSeeAnotherPatient_Returns404()
    {
        var other = await CreatePatientWithProfileAsync();

        var response = await ClientFor(PatientToken(Unique("pat"))).GetAsync($"/api/patients/{other}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Doctor_WithAppointment_SeesPatient_AndAuthorizationIsForwarded()
    {
        var sub = await CreatePatientWithProfileAsync();
        var doctorId = Unique("doc");
        FakeDownstream.RegisterAppointment(sub, doctorId);
        var token = TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);

        var response = await ClientFor(token).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ankara", (await ReadAsync<PatientProfileDto>(response)).City);
        var call = Assert.Single(FakeDownstream.Calls, c => c.PathAndQuery == $"/api/appointments?patientId={sub}" && c.Authorization == "Bearer " + token);
        Assert.NotNull(call);
    }

    [Fact]
    public async Task Doctor_WithAppointment_ButPatientHasNoProfile_Gets200EmptyProfile()
    {
        var sub = Unique("pat");
        var doctorId = Unique("doc");
        FakeDownstream.RegisterAppointment(sub, doctorId);

        var response = await ClientFor(DoctorToken(doctorId)).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadAsync<PatientProfileDto>(response);
        Assert.Equal(sub, profile.UserId);
        Assert.Null(profile.City);
    }

    [Fact]
    public async Task Doctor_WithoutAppointment_Returns404()
    {
        var sub = await CreatePatientWithProfileAsync();

        var response = await ClientFor(DoctorToken(Unique("doc"))).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Doctor_WhosePatientBelongsToAnotherDoctor_Returns404()
    {
        var sub = await CreatePatientWithProfileAsync();
        FakeDownstream.RegisterAppointment(sub, Unique("other-doc"));

        var response = await ClientFor(DoctorToken(Unique("doc"))).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Doctor_TokenWithoutDoctorId_Returns404_WithoutCallingAppointment()
    {
        var sub = await CreatePatientWithProfileAsync();
        var before = FakeDownstream.CallCount("/api/appointments");

        var response = await ClientFor(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor])).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, FakeDownstream.CallCount("/api/appointments"));
    }

    [Theory]
    [InlineData(DownstreamMode.Down)]
    [InlineData(DownstreamMode.Timeout)]
    [InlineData(DownstreamMode.Error500)]
    public async Task Doctor_WhenAppointmentServiceIsUnavailable_Returns503(DownstreamMode mode)
    {
        var sub = await CreatePatientWithProfileAsync();
        var doctorId = Unique("doc");
        FakeDownstream.RegisterAppointment(sub, doctorId);
        FakeDownstream.AppointmentMode = mode;

        var response = await ClientFor(DoctorToken(doctorId)).GetAsync($"/api/patients/{sub}", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Admin_And_Patient_AreNotAffectedByAppointmentOutage()
    {
        var sub = await CreatePatientWithProfileAsync();
        FakeDownstream.AppointmentMode = DownstreamMode.Down;

        Assert.Equal(HttpStatusCode.OK, (await ClientFor(AdminToken()).GetAsync($"/api/patients/{sub}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(PatientToken(sub)).GetAsync($"/api/patients/{sub}", Ct)).StatusCode);
    }

    [Fact]
    public async Task PatientIdWithSpecialCharacters_IsEscapedInDownstreamQuery()
    {
        var doctorId = Unique("doc");
        const string weird = "a&patientId=b c";
        FakeDownstream.RegisterAppointment(weird, doctorId);

        var response = await ClientFor(DoctorToken(doctorId)).GetAsync($"/api/patients/{Uri.EscapeDataString(weird)}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(FakeDownstream.Calls, c => c.PathAndQuery == "/api/appointments?patientId=a%26patientId%3Db%20c");
    }

    [Fact]
    public async Task ResponseUsesTextEnums()
    {
        var sub = await CreatePatientWithProfileAsync();

        var body = await (await ClientFor(AdminToken()).GetAsync($"/api/patients/{sub}", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.Contains("\"gender\":\"" + Gender.Female + "\"", body);
        Assert.Contains("\"bloodType\":\"" + BloodType.APos + "\"", body);
    }
}
