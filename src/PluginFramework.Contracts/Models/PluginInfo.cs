using PluginFramework.Contracts.Base;

namespace PluginFramework.Contracts.Models;

public class PluginInfo
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public Version? InterfaceVersion { get; init; }
    public PluginCapabilities Capabilities { get; init; }
    public DateTime LoadTime { get; init; }
    public List<string> Warnings { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();

    public bool HasUI => Capabilities.HasFlag(PluginCapabilities.UserInterface);
    public bool IsScheduled => Capabilities.HasFlag(PluginCapabilities.Scheduled);
    public bool HasHttp => Capabilities.HasFlag(PluginCapabilities.HttpService);
    public bool HasDatabase => Capabilities.HasFlag(PluginCapabilities.DatabaseAccess);
}
