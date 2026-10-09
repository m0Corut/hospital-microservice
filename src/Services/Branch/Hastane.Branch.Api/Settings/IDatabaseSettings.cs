namespace Hastane.Branch.Api.Settings;

public interface IDatabaseSettings
{
    string ConnectionString { get; set; }
    string DatabaseName { get; set; }
    string BranchCollectionName { get; set; }
}
