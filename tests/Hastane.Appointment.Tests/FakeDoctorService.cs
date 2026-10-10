using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Appointment.Tests;

public enum DoctorMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
}

public sealed record FakeEntry(DayOfWeek Day, string Start, string End, int SlotMinutes);

public sealed record FakeClinic(string Name, string City, params FakeEntry[] Schedule);

public sealed record DoctorCall(string Path, string? Authorization);

// Doctor servisinin sahte hali (Payment testlerindeki FakeDownstream deseni): gerçek ağ yok, Docker yok.
// Factory başına TEK örnek; durum örnekte tutulur ki sınıflar paralel koşunca birbirini bozmasın.
// Kayıtlı olmayan doktor için "her saati kabul eden" geniş program döner: mevcut testler doktoru önceden
// kaydetmeden çalışır, yeni testler Register ile gerçek kısıtı kurar.
public sealed class FakeDoctorService
{
    private readonly ConcurrentDictionary<string, FakeClinic[]?> _doctors = new();
    private readonly ConcurrentQueue<DoctorCall> _calls = new();

    public DoctorMode Mode { get; set; } = DoctorMode.Normal;

    public IReadOnlyList<DoctorCall> Calls => _calls.ToArray();

    // null => 404 (böyle bir doktor yok).
    public void Register(string doctorId, params FakeClinic[]? clinics) => _doctors[doctorId] = clinics;

    public void Reset() => Mode = DoctorMode.Normal;

    // 7 gün, 00:00-24:00, 1 dakikalık slot: her dakika geçerli bir slot başlangıcı.
    private static readonly FakeClinic[] Broad =
    [
        new("Test Klinik", "Ankara",
            Enum.GetValues<DayOfWeek>().Select(d => new FakeEntry(d, "00:00", "24:00", 1)).ToArray()),
    ];

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FakeDoctorService owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            owner._calls.Enqueue(new DoctorCall(path, request.Headers.Authorization?.ToString()));

            switch (owner.Mode)
            {
                case DoctorMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case DoctorMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
                case DoctorMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            // Gerçek Doctor'da GET {id} giriş ister.
            if (request.Headers.Authorization is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            var id = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
            var clinics = owner._doctors.TryGetValue(id, out var registered) ? registered : Broad;
            if (clinics is null) return new HttpResponseMessage(HttpStatusCode.NotFound);

            // Doctor'ın GetByIdDoctorDto biçimi: camelCase, enum metin.
            var body = JsonSerializer.Serialize(new
            {
                id,
                clinicLocations = clinics.Select(c => new
                {
                    clinicName = c.Name,
                    city = c.City,
                    schedule = c.Schedule.Select(s => new
                    {
                        dayOfWeek = s.Day.ToString(),
                        startTime = s.Start,
                        endTime = s.End,
                        slotMinutes = s.SlotMinutes,
                    }),
                }),
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
