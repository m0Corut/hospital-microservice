namespace Hastane.Notification.Api.Settings;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string NotificationCollectionName { get; set; } = "notifications";
    public string EmailLogCollectionName { get; set; } = "email_logs";
}
