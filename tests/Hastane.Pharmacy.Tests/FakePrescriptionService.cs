using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Pharmacy.Tests;

public enum PrescriptionMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
    Forbidden403,
    BadJson,
}

public sealed record PrescriptionCall(string Path, string? Authorization);

// Prescription servisinin sahte hali: gerçek ağ yok. Pharmacy'nin typed HttpClient'ının altına HttpMessageHandler olarak takılır.
// Gerçek servis gibi davranır: Authorization başlığı yoksa 401, kayıt yoksa 404.
// Testler seri çalışır; durum statik, her testin kendi (benzersiz) reçete kimlikleri vardır.
public static class FakePrescriptionService
{
    private static readonly ConcurrentDictionary<Guid, (string PatientId, string DoctorId, string[] Items)> Prescriptions = new();
    private static readonly ConcurrentQueue<PrescriptionCall> CallLog = new();

    public static PrescriptionMode Mode { get; set; } = PrescriptionMode.Normal;

    public static IReadOnlyList<PrescriptionCall> Calls => CallLog.ToArray();

    public static int CallCount(Guid prescriptionId)
        => CallLog.Count(c => c.Path.EndsWith("/" + prescriptionId.ToString("D"), StringComparison.Ordinal));

    public static void Reset() => Mode = PrescriptionMode.Normal;

    public static Guid Register(params string[] medicineNames)
    {
        var id = Guid.CreateVersion7();
        Prescriptions[id] = ("pat-" + Guid.NewGuid().ToString("N")[..8], "doc-" + Guid.NewGuid().ToString("N")[..8], medicineNames);
        return id;
    }

    public static HttpMessageHandler CreateHandler() => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            CallLog.Enqueue(new PrescriptionCall(path, request.Headers.Authorization?.ToString()));

            switch (Mode)
            {
                case PrescriptionMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case PrescriptionMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
                case PrescriptionMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                case PrescriptionMode.Forbidden403: return new HttpResponseMessage(HttpStatusCode.Forbidden);
                case PrescriptionMode.BadJson: return Json("<html>not json");
            }

            if (request.Headers.Authorization is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            var raw = path[(path.LastIndexOf('/') + 1)..];
            if (!Guid.TryParse(raw, out var id) || !Prescriptions.TryGetValue(id, out var p))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            // Gerçek Prescription servisinin GetByIdPrescriptionDto biçimi.
            return Json(JsonSerializer.Serialize(new
            {
                id,
                appointmentId = Guid.NewGuid(),
                doctorId = p.DoctorId,
                patientId = p.PatientId,
                issuedAt = DateTime.UtcNow,
                notes = (string?)null,
                items = p.Items.Select((name, i) => new
                {
                    id = Guid.NewGuid(),
                    lineNo = i + 1,
                    medicineName = name,
                    dosage = "1x1",
                    frequency = "Günde 1",
                    durationDays = 5,
                    instructions = (string?)null,
                }),
            }));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
