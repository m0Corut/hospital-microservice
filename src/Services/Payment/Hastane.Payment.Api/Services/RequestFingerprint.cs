using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hastane.Payment.Api.Dtos;

namespace Hastane.Payment.Api.Services;

// Gövde özeti: tutar/randevu/hasta/doktor/para birimi. Kart verisi bilerek DAHİL DEĞİL (hash'i bile saklanmaz).
// JSON dizisi: alanlar ayraç karakteriyle birleştirilmez, böylece "a|b","c" ile "a","b|c" aynı özeti vermez.
public static class RequestFingerprint
{
    public static string Compute(CreatePaymentDto dto)
    {
        var canonical = JsonSerializer.Serialize(new[]
        {
            dto.AppointmentId.ToString("D"), dto.PatientId, dto.DoctorId,
            dto.Amount.ToString("F2", CultureInfo.InvariantCulture), dto.Currency,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
