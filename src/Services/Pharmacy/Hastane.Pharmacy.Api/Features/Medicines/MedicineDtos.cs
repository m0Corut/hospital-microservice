using System.Linq.Expressions;
using Hastane.Pharmacy.Api.Entities;

namespace Hastane.Pharmacy.Api.Features.Medicines;

// OKUMA modeli: entity'nin kopyası değil, ekranın/istemcinin ihtiyacı olan şekil. Sorgular bunu doğrudan SQL SELECT'ine çevirir.
public sealed record MedicineDto(
    Guid Id,
    string Name,
    string ActiveIngredient,
    MedicineForm Form,
    string Strength,
    string Barcode,
    decimal Price,
    string Currency,
    int Stock,
    int MinStock,
    bool IsLowStock,
    bool RequiresPrescription,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    byte[] RowVersion)
{
    public const string CurrencyCode = "TRY";
}

public sealed record StockMovementDto(
    Guid Id,
    Guid MedicineId,
    StockMovementType Type,
    int Delta,
    int StockAfter,
    string Reason,
    Guid? PrescriptionId,
    string? CreatedBy,
    DateTime CreatedAt);

public static class MedicineProjection
{
    // Tek ifade, iki kullanım: sorgular IQueryable.Select(Projection) ile SQL'e çevirir (entity yüklenmez, AsNoTracking gerekmez),
    // yazma handler'ları az sayıda satır için derlenmiş sürümü (ToDto) kullanır.
    public static readonly Expression<Func<Medicine, MedicineDto>> Projection = m => new MedicineDto(
        m.Id, m.Name, m.ActiveIngredient, m.Form, m.Strength, m.Barcode, m.Price, MedicineDto.CurrencyCode,
        m.Stock, m.MinStock, m.Stock <= m.MinStock, m.RequiresPrescription, m.IsActive, m.CreatedAt, m.UpdatedAt, m.RowVersion);

    private static readonly Func<Medicine, MedicineDto> Compiled = Projection.Compile();

    public static MedicineDto ToDto(Medicine medicine) => Compiled(medicine);

    public static readonly Expression<Func<StockMovement, StockMovementDto>> MovementProjection = s => new StockMovementDto(
        s.Id, s.MedicineId, s.Type, s.Delta, s.StockAfter, s.Reason, s.PrescriptionId, s.CreatedBy, s.CreatedAt);

    private static readonly Func<StockMovement, StockMovementDto> CompiledMovement = MovementProjection.Compile();

    public static StockMovementDto ToDto(StockMovement movement) => CompiledMovement(movement);
}
