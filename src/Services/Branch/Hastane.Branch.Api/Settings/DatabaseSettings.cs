namespace Hastane.Branch.Api.Settings;

public class DatabaseSettings : IDatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string BranchCollectionName { get; set; } = string.Empty;
}
