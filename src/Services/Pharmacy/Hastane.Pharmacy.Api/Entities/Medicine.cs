namespace Hastane.Pharmacy.Api.Entities;

// YAZMA modeli: komutlar (Create/Update/Delete) bu entity üzerinden çalışır. Okuma tarafı entity'yi hiç yüklemez,
// doğrudan DTO'ya projeksiyon yapar (bkz. Features/Medicines/MedicineProjection.cs).
public class Medicine
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    // Türkçe harf ve büyük/küçük harf farkı silinmiş ad ("Ağrı KESİCİ" => "agri kesici"). Karşılamada reçetedeki ad
    // veritabanında bununla eşleşir; elle girilmez, Name değişince handler yeniden hesaplar.
    public required string NormalizedName { get; set; }

    public required string ActiveIngredient { get; set; }
    public required string NormalizedIngredient { get; set; }
    public MedicineForm Form { get; set; }
    public required string Strength { get; set; }
    public required string Barcode { get; set; }

    // TRY, 2 ondalık.
    public decimal Price { get; set; }

    // Eksiye düşemez: hem koşullu UPDATE hem veritabanı CHECK kısıtı koruyor.
    public int Stock { get; set; }
    public int MinStock { get; set; }
    public bool RequiresPrescription { get; set; }

    // Silme "yumuşak": hareket ve karşılama kayıtları ilaca bağlı kaldığı için satır silinmez.
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // SQL Server her yazmada otomatik artırır; PUT'ta istemcinin gönderdiği değerle karşılaştırılır (iyimser eşzamanlılık).
    public byte[] RowVersion { get; set; } = [];
}
