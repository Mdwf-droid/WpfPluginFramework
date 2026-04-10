using Microsoft.Extensions.Logging;
using Quartz;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Models;
using PluginFramework.Contracts.Plugins;
using PluginFramework.Contracts.Versioning;

namespace Plugins.Demo.Scheduler;

[Plugin(Id = "demo-scheduler", Name = "Plugin Scheduler Démo", Version = "1.0.0")]
[InterfaceVersion(2, 0, 0)]
public class DemoSchedulerPlugin : IScheduledPlugin
{
    private IPluginHost _host = null!;
    private ILogger _logger = null!;
    private int _executionCount;

    public string PluginId => "demo-scheduler";
    public string Name => "Plugin Scheduler Démo";
    public string Version => "1.0.0";
    public Version InterfaceVersion => new(2, 0, 0);
    public PluginCapabilities Capabilities => PluginCapabilities.Scheduled;
    public bool ShouldRetryOnFailure => true;
    public int MaxRetries => 3;

    public Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        // Utiliser le logger bridge dédié → les logs remontent au Host
        _logger = host.GetPluginLogger(PluginId);
        _logger.LogInformation("DemoSchedulerPlugin initialisé");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _logger.LogInformation("DemoSchedulerPlugin arrêté ({Count} exécutions)", _executionCount);
        return Task.CompletedTask;
    }

    public async Task ExecuteAsync(IJobExecutionContext context)
    {
        _executionCount++;

        // Ce log apparaîtra dans le panneau de logs du Host
        _logger.LogInformation(
            "⏱️ Exécution #{Count} à {Time} (prochain: {Next})",
            _executionCount,
            DateTime.Now.ToString("HH:mm:ss"),
            context.NextFireTimeUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "?");

        await Task.Delay(500);

        // Notification toutes les 5 exécutions → bannière dans le Host
        if (_executionCount % 5 == 0)
        {
            _host.Notify(new PluginNotification
            {
                PluginId = PluginId,
                Title = "Jalon atteint",
                Message = $"{_executionCount} exécutions planifiées complétées !",
                Level = NotificationLevel.Success,
                Data = { ["ExecutionCount"] = _executionCount }
            });
        }

        // Warning si exécution longue (simulé)
        if (_executionCount % 7 == 0)
        {
            _logger.LogWarning("Exécution plus longue que prévu (simulé)");
            _host.Notify(new PluginNotification
            {
                PluginId = PluginId,
                Title = "Performance",
                Message = "L'exécution #" + _executionCount + " a pris plus longtemps que prévu",
                Level = NotificationLevel.Warning
            });
        }
    }

    public IReadOnlyList<ITrigger> GetTriggers()
    {
        return new[]
        {
            TriggerBuilder.Create()
                .WithIdentity($"{PluginId}.every15s", "Plugins")
                .StartNow()
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(15).RepeatForever())
                .WithDescription("Toutes les 15 secondes")
                .Build()
        };
    }
}
