using Microsoft.Extensions.Logging;
using PluginFramework.Contracts.Models;

namespace PluginFramework.Contracts.Base;

public interface IPluginHost
{
    ILogger Logger { get; }
    IServiceProvider Services { get; }
    T? GetService<T>() where T : class;
    void RegisterService<T>(T service) where T : class;

    /// <summary>Obtenir un logger dédié au plugin (ses logs remontent au Host)</summary>
    ILogger GetPluginLogger(string pluginId);

    /// <summary>Envoyer une notification visible dans le Host</summary>
    void Notify(PluginNotification notification);
}