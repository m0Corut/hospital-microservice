using System.Linq.Expressions;
using Hastane.Pharmacy.Api.Entities;

namespace Hastane.Pharmacy.Api.Features.Dispensing;

public enum DispenseLineStatus
{
    Matched,           // katalogda tek ilaçla eşleşti ve stok yeterli
    NotFound,          // katalogda bu adla ilaç yok
    Ambiguous,         // aynı ad birden fazla katalog ilacıyla eşleşiyor (hangisi belli değil)
    InsufficientStock, // eşleşti ama stok yetmiyor
}

// Reçetenin bir kalemi için karşılama planı: ne yazıyor, hangi ilaçla eşleşti, ne kadar lazım, ne kadar var.
public sealed record DispenseLineDto(
    int LineNo,
    string PrescriptionMedicineName,
    Guid? MedicineId,
    string? MedicineName,
    int Required,
    int Available,
    DispenseLineStatus Status);

public sealed record DispensePreviewDto(
    Guid PrescriptionId,
    string PatientId,
    string DoctorId,
    bool AlreadyDispensed,
    bool CanDispense,
    IReadOnlyList<DispenseLineDto> Lines);

public sealed record DispensationLineDto(
    int LineNo,
    string PrescriptionMedicineName,
    Guid MedicineId,
    string MedicineName,
    int Quantity,
    decimal UnitPrice);

public sealed record DispensationDto(
    Guid Id,
    Guid PrescriptionId,
    string PatientId,
    string DoctorId,
    string? DispensedBy,
    DateTime DispensedAt,
    List<DispensationLineDto> Lines)
{
    public string Currency => "TRY";
    public decimal Total => Lines.Sum(l => l.UnitPrice * l.Quantity);
}

public static class DispensationProjection
{
    public static readonly Expression<Func<Dispensation, DispensationDto>> Projection = d => new DispensationDto(
        d.Id, d.PrescriptionId, d.PatientId, d.DoctorId, d.DispensedBy, d.DispensedAt,
        d.Lines.OrderBy(l => l.LineNo)
            .Select(l => new DispensationLineDto(l.LineNo, l.PrescriptionMedicineName, l.MedicineId, l.MedicineName, l.Quantity, l.UnitPrice))
            .ToList());

    private static readonly Func<Dispensation, DispensationDto> Compiled = Projection.Compile();

    public static DispensationDto ToDto(Dispensation dispensation) => Compiled(dispensation);
}
