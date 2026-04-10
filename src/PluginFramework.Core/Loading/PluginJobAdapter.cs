using Quartz;
using PluginFramework.Contracts.Plugins;
using Microsoft.Extensions.Logging;

namespace PluginFramework.Core.Loading;

/// <summary>
/// Adapteur Quartz qui fait le pont entre l'IScheduler et un IScheduledPlugin.
/// </summary>
public class PluginJobAdapter : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var pluginId = context.MergedJobDataMap.GetString("PluginId");
        var manager = (AdvancedPluginManager)context.MergedJobDataMap["PluginManager"];

        var plugin = manager.GetPlugin<IScheduledPlugin>(pluginId!);
        if (plugin == null)
        {
            throw new JobExecutionException($"Plugin '{pluginId}' non trouvé ou non ordonnançable");
        }

        try
        {
            await plugin.ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            manager.Logger.LogError(ex, "Erreur lors de l'exécution planifiée du plugin '{PluginId}'", pluginId);

            if (plugin.ShouldRetryOnFailure)
            {
                var refireCount = context.RefireCount;
                if (refireCount < plugin.MaxRetries)
                    throw new JobExecutionException(ex, refireImmediately: true);
            }

            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
