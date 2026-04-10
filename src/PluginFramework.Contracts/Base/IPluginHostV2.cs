using Microsoft.Extensions.Configuration;
using PluginFramework.Contracts.Services;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Base;

[InterfaceVersion(2, 0, 0)]
public interface IPluginHostV2 : IPluginHost
{
    IConfiguration Configuration { get; }
    IEventAggregator EventBus { get; }
    Task<bool> CheckPluginHealthAsync(string pluginId);
}
