namespace Hastane.Doctor.Api.Settings;

public interface IDatabaseSettings
{
    string ConnectionString { get; set; }
    string DatabaseName { get; set; }
    string DoctorCollectionName { get; set; }
}
