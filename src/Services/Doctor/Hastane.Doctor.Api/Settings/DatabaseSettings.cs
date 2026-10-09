namespace Hastane.Doctor.Api.Settings;

public class DatabaseSettings : IDatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DoctorCollectionName { get; set; } = string.Empty;
}
