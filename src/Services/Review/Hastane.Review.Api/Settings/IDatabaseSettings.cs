namespace Hastane.Review.Api.Settings;

public interface IDatabaseSettings
{
    string ConnectionString { get; set; }
    string DatabaseName { get; set; }
    string ReviewCollectionName { get; set; }
}
