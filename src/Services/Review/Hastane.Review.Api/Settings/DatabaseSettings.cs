namespace Hastane.Review.Api.Settings;

public class DatabaseSettings : IDatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string ReviewCollectionName { get; set; } = string.Empty;
}
