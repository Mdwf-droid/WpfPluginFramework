namespace PluginFramework.Contracts.Models;

public class PluginNotification
{
    public required string PluginId { get; init; }
    public required string Title { get; init; }
    public string? Message { get; init; }
    public NotificationLevel Level { get; init; } = NotificationLevel.Info;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public Dictionary<string, object?> Data { get; init; } = new();
}

public enum NotificationLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}
