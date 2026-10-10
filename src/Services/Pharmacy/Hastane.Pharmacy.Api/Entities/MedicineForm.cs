namespace Hastane.Pharmacy.Api.Entities;

// JSON'da ve veritabanında metin olarak tutulur ("Tablet"); sayı sırası değişirse eski kayıtlar bozulmasın.
// Arayüz Türkçe karşılıklarını gösterir: Tablet, Şurup, Krem, Ampul, Kapsül, Diğer.
public enum MedicineForm
{
    Tablet,
    Syrup,
    Cream,
    Ampoule,
    Capsule,
    Other,
}
