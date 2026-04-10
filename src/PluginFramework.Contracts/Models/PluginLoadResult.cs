namespace PluginFramework.Contracts.Models;

public class PluginLoadResult
{
    public bool Success { get; set; }
    public string? PluginId { get; set; }
    public string? PluginPath { get; set; }
    public Exception? Exception { get; set; }
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public TimeSpan LoadDuration { get; set; }
}
