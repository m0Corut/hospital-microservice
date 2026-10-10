namespace Hastane.WebUI.Models.Api.Pharmacy;

// Pharmacy servisinin DTO'larının WebUI kopyası (elle yazıldı). camelCase gelir; enum metin gelir.

public sealed class PharmacyMedicineApiModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ActiveIngredient { get; set; } = string.Empty;
    public string Form { get; set; } = "Tablet";
    public string Strength { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public int Stock { get; set; }
    public int MinStock { get; set; }
    public bool IsLowStock { get; set; }
    public bool RequiresPrescription { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Base64 metin: düzenleme formunda saklanıp PUT'ta aynen geri gönderilir (eşzamanlılık belirteci).
    public string? RowVersion { get; set; }
}

public sealed class PharmacyMedicineWriteBody
{
    public string Name { get; set; } = string.Empty;
    public string ActiveIngredient { get; set; } = string.Empty;
    public string Form { get; set; } = "Tablet";
    public string Strength { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal Price { get; set; }

    // Yalnız oluşturmada gönderilir; güncellemede stok değişmez (stok ayrı uçtan).
    public int? Stock { get; set; }
    public int MinStock { get; set; }
    public bool RequiresPrescription { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class PharmacyStockAdjustBody
{
    public int Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PharmacyMovementApiModel
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Delta { get; set; }
    public int StockAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? PrescriptionId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PharmacyDispenseLineApiModel
{
    public int LineNo { get; set; }
    public string PrescriptionMedicineName { get; set; } = string.Empty;
    public Guid? MedicineId { get; set; }
    public string? MedicineName { get; set; }
    public int Required { get; set; }
    public int Available { get; set; }

    // Matched / NotFound / Ambiguous / InsufficientStock
    public string Status { get; set; } = string.Empty;
}

public sealed class PharmacyDispensePreviewApiModel
{
    public Guid PrescriptionId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public bool AlreadyDispensed { get; set; }
    public bool CanDispense { get; set; }
    public List<PharmacyDispenseLineApiModel> Lines { get; set; } = [];
}

public sealed class PharmacyDispensationLineApiModel
{
    public int LineNo { get; set; }
    public string PrescriptionMedicineName { get; set; } = string.Empty;
    public Guid MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class PharmacyDispensationApiModel
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string? DispensedBy { get; set; }
    public DateTime DispensedAt { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Total { get; set; }
    public List<PharmacyDispensationLineApiModel> Lines { get; set; } = [];
}
