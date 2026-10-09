namespace Hastane.Appointment.Api.Entities;

// Veritabanında sayı (0,1,2,3) değil metin ("Pending"...) saklanır: sıra değişse ya da araya yeni değer girse
// eski satırların anlamı kaymaz, SQL'e bakan biri de ne olduğunu okuyabilir (bkz. AppointmentConfiguration).
public enum AppointmentStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
}
