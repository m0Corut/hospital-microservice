using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Tests;

public static class TestSupport
{
    // Servis enum'ları JSON'da metin ister; test istemcisi de aynı biçimde konuşur.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static ScheduleSlotDto Slot(DayOfWeek day, string start = "09:00", string end = "12:00", int minutes = 30)
        => new() { DayOfWeek = day, StartTime = start, EndTime = end, SlotMinutes = minutes };

    public static ClinicLocationDto Loc(string city = "Ankara", decimal? fee = null, string clinic = "Merkez Klinik",
        string currency = "TRY", params ScheduleSlotDto[] slots)
        => new() { ClinicName = clinic, City = city, HourlyFee = fee, Currency = currency, Schedule = [.. slots] };

    public static CreateDoctorDto NewDoc(string first = "Ayşe", string last = "Test", string branch = "b",
        DoctorGender gender = DoctorGender.Female, params ClinicLocationDto[] locations)
        => new()
        {
            FirstName = first,
            LastName = last,
            Title = "Uzm. Dr.",
            Gender = gender,
            BranchId = branch,
            Phone = "0555 000 00 00",
            Email = "gizli@hastane.test",
            ClinicLocations = [.. locations],
        };

    public static async Task<ResultDoctorDto> CreateOk(this HttpClient client, CreateDoctorDto dto, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/doctors", dto, Json, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultDoctorDto>(Json, ct))!;
    }

    public static async Task<PagedResult<DoctorSearchItemDto>> SearchOk(this HttpClient client, string query, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/doctors/search" + query, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<DoctorSearchItemDto>>(Json, ct))!;
    }

    public static string NewTag() => "t" + Guid.NewGuid().ToString("N")[..12];
}
