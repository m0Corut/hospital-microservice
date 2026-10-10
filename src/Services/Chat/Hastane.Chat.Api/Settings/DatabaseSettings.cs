namespace Hastane.Chat.Api.Settings;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string ConversationCollectionName { get; set; } = string.Empty;
    public string MessageCollectionName { get; set; } = string.Empty;
}
