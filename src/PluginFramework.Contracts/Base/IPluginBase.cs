using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Base;

[InterfaceVersion(2, 0, 0)]
public interface IPluginBase : IVersionedInterface
{
    string PluginId { get; }
    string Name { get; }
    string Version { get; }
    PluginCapabilities Capabilities { get; }

    Task InitializeAsync(IPluginHost host);
    Task ShutdownAsync();
}
