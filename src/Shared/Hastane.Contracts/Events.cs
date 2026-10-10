namespace Hastane.Contracts;

// ÖĞRETİM: Bu dosya servisler arasındaki "olay sözleşmesi"dir. Yayınlayan da tüketen de BU projeye bakar, birbirinin koduna değil.
// Kural: olaylar GEÇMİŞ ZAMAN adlı, değişmez (record) ve MİNİMUM veri taşıyan kayıtlardır. Ad, telefon, tanı gibi kişisel veri yok;
// yalnız kimlik ve tarih var. Tüketici fazlasını isterse kimliğiyle ilgili servise sorar.
// Durum alanları enum değil metindir: yayıncı enum'a değer eklerse tüketici derlemesi/çözümlemesi kırılmasın.
// Sözleşmeye alan EKLEMEK güvenli (eski tüketici yok sayar), alan SİLMEK/yeniden adlandırmak kırıcıdır.

// "Aynı olay iki kez gelirse tek sonuç" için EventId: yayıncı üretir, tüketici tekilleştirmede kullanır.
public record AppointmentCreated
{
    public Guid EventId { get; init; }
    public Guid AppointmentId { get; init; }
    public string PatientUserId { get; init; } = string.Empty;
    public string DoctorId { get; init; } = string.Empty;
    public DateTime AppointmentDate { get; init; }
}

// ChangedByRole: Patient | Doctor | Admin (değişikliği yapanın rolü; alıcı kuralı buna göre seçilir).
public record AppointmentStatusChanged
{
    public Guid EventId { get; init; }
    public Guid AppointmentId { get; init; }
    public string PatientUserId { get; init; } = string.Empty;
    public string DoctorId { get; init; } = string.Empty;
    public DateTime AppointmentDate { get; init; }
    public string OldStatus { get; init; } = string.Empty;
    public string NewStatus { get; init; } = string.Empty;
    public string ChangedByRole { get; init; } = string.Empty;
}

public record AppointmentRescheduled
{
    public Guid EventId { get; init; }
    public Guid AppointmentId { get; init; }
    public string PatientUserId { get; init; } = string.Empty;
    public string DoctorId { get; init; } = string.Empty;
    public DateTime AppointmentDate { get; init; }
    public DateTime OldDate { get; init; }
    public DateTime NewDate { get; init; }
}

public record OrderPlaced
{
    public Guid EventId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string PatientUserId { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public record OrderCancelled
{
    public Guid EventId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string PatientUserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
