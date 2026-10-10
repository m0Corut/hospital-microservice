namespace Hastane.Patient.Api.Entities;

// Veritabanında ve JSON'da metin olarak ("Male"); sayı değil (sıra değişirse eski kayıtlar yanlış okunmasın).
public enum Gender
{
    Male,
    Female,
    Other,
}
