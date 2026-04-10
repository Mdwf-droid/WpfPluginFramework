namespace PluginFramework.Contracts.Models;

public class PluginLogEntry
{
    public required string PluginId { get; init; }
    public required string PluginName { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] [{Level}] [{PluginName}] {Message}";
}
