using Quartz;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Plugins;

[InterfaceVersion(1, 0, 0)]
public interface IScheduledPlugin : IPluginBase
{
    Task ExecuteAsync(IJobExecutionContext context);
    IReadOnlyList<ITrigger> GetTriggers();
    
    /// <summary>Indique si le job doit survivre au redémarrage (durable)</summary>
    bool IsDurable => false;

    /// <summary>Indique si le job doit être ré-exécuté en cas d'échec</summary>
    bool ShouldRetryOnFailure => true;

    /// <summary>Nombre max de tentatives</summary>
    int MaxRetries => 3;
}
