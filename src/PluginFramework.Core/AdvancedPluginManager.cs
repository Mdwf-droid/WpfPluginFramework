using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginFramework.Configuration;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Models;
using PluginFramework.Contracts.Plugins;
using PluginFramework.Contracts.Services;
using PluginFramework.Core.Discovery;
using PluginFramework.Core.Loading;
using PluginFramework.Core.Logging;
using PluginFramework.Core.Watching;
using PluginFramework.Resilience;
using PluginFramework.Services.Database;
using PluginFramework.Services.Events;
using Quartz;
using Quartz.Impl;
using System.IO;
using System.Reflection;
using System.Windows;

namespace PluginFramework.Core;

public class AdvancedPluginManager : IPluginHostV2, IAsyncDisposable
{
    // ── État interne ──
    private readonly Dictionary<string, LoadedPlugin> _plugins = new();
    private readonly Dictionary<Type, object> _services = new();
    private readonly object _pluginLock = new();

    // ── Dépendances ──
    private readonly ILogger<AdvancedPluginManager> _logger;
    private readonly PluginSettings _settings;
    private readonly SharedDatabaseService _database;
    private readonly ResilientPluginLoader _loader;
    private readonly PluginDiscoveryService _discoveryService;
    private readonly PluginFileWatcher? _fileWatcher;
    private readonly IScheduler _scheduler;
    private readonly IServiceCollection _serviceCollection;
    private IServiceProvider _serviceProvider;
    private WebApplication? _webApp;
    private readonly Dictionary<string, IHttpServicePlugin> _httpPlugins = new();
    private bool _webAppStarted;

    private readonly Dictionary<string, PluginBridgeLogger> _pluginLoggers = new();
    private readonly ILoggerFactory _loggerFactory;

    // ── IPluginHost ──
    public ILogger Logger => _logger;
    public IServiceProvider Services => _serviceProvider;
    public IConfiguration Configuration { get; }
    public IEventAggregator EventBus { get; }
    public PluginSettings PluginSettings => _settings;

    // ── Événements ──
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;
    public event EventHandler<PluginEventArgs>? PluginUnloaded;
    public event EventHandler<PluginErrorEventArgs>? PluginError;

    public AdvancedPluginManager(PluginSettings settings, IConfiguration configuration)
    {
        _settings = settings;
        Configuration = configuration;

        // ── Logging ──
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<AdvancedPluginManager>();

        // ── Data directory ──
        var dataDir = _settings.DataDirectory ??
                      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginData");
        Directory.CreateDirectory(dataDir);

        // ── Database ──
        _database = new SharedDatabaseService(Path.Combine(dataDir, "plugins.db"), _logger);

        // ── Résilience ──
        _loader = new ResilientPluginLoader(_logger, _settings);

        // ── Discovery ──
        _discoveryService = new PluginDiscoveryService(_settings, _logger);

        // ── Quartz ──
        var schedulerFactory = new StdSchedulerFactory();
        _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        _scheduler.Start().GetAwaiter().GetResult();
        _logger.LogInformation("Quartz Scheduler démarré");

        // ── DI ──
        _serviceCollection = new ServiceCollection();
        _serviceCollection.AddSingleton<IPluginHost>(this);
        _serviceCollection.AddSingleton<IPluginHostV2>(this);
        _serviceCollection.AddSingleton<IPluginDatabase>(_database);
        _serviceCollection.AddSingleton<IConfiguration>(configuration);
        _serviceProvider = _serviceCollection.BuildServiceProvider();

        // ── EventBus ──
        EventBus = new SimpleEventAggregator();

        // ── File Watcher ──
        if (_settings.EnableFileWatching)
        {
            _fileWatcher = new PluginFileWatcher(this, _settings, _logger);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  DISCOVER & LOAD
    // ═══════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<PluginLoadResult>> DiscoverAndLoadAllAsync()
    {
        _logger.LogInformation("═══ Début de la découverte et du chargement des plugins ═══");

        var discovered = _discoveryService.DiscoverAll();
        var results = new List<PluginLoadResult>();

        foreach (var file in discovered)
        {
            var result = await LoadPluginFromFullPathAsync(file.FullPath, file.DirectoryRoot);
            results.Add(result);
        }

        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);
        _logger.LogInformation("═══ Découverte terminée: {Success} chargé(s), {Fail} échoué(s) ═══",
            successCount, failCount);

        // Activer la surveillance si configurée
        if (_fileWatcher != null && _settings.EnableFileWatching)
        {
            _fileWatcher.StartWatching(_settings.Directories);
        }

        return results;
    }

    public async Task<PluginLoadResult> LoadPluginFromFullPathAsync(string pluginPath, string? rootDirectory = null)
    {
        var result = await _loader.LoadPluginSafelyAsync(pluginPath, async path =>
        {
            // Créer le contexte isolé avec shadow copy
            var context = new PluginLoadContext(path);
            var shadowPluginPath = Path.Combine(context.ShadowCopyPath, Path.GetFileName(path));
            var assembly = context.LoadFromAssemblyPath(shadowPluginPath);

            // Trouver le type plugin
            var pluginType = FindPluginType(assembly);
            if (pluginType == null)
            {
                context.Unload();
                context.CleanupShadowCopy();
                throw new InvalidOperationException(
                    $"Aucun type implémentant IPluginBase trouvé dans '{Path.GetFileName(path)}'");
            }

            // Instancier
            var plugin = (IPluginBase)Activator.CreateInstance(pluginType)!;

            // Créer le logger bridge pour ce plugin AVANT l'init
            var pluginInnerLogger = _loggerFactory.CreateLogger($"Plugin.{plugin.PluginId}");
            var bridgeLogger = new PluginBridgeLogger(plugin.PluginId, plugin.Name, pluginInnerLogger, EventBus);
            lock (_pluginLock)
            {
                _pluginLoggers[plugin.PluginId] = bridgeLogger;
            }

            // Initialiser (thread UI si plugin UI)
            if (plugin is IUIPlugin)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                    await plugin.InitializeAsync(this));
            }
            else
            {
                await plugin.InitializeAsync(this);
            }

            // Créer l'entrée LoadedPlugin
            var loaded = new LoadedPlugin
            {
                Plugin = plugin,
                Context = context,
                Assembly = assembly,
                LoadTimeUtc = DateTime.UtcNow
            };

            loaded.Metadata["PluginPath"] = path;
            if (rootDirectory != null) loaded.Metadata["RootDirectory"] = rootDirectory;

            // Configurer selon les capacités
            await ConfigurePluginCapabilitiesAsync(plugin, loaded);

            // Enregistrer
            lock (_pluginLock)
            {
                _plugins[plugin.PluginId] = loaded;
            }

            // Persister en base
            await _database.RegisterPluginAsync(plugin.PluginId, plugin.Name, plugin.Version);
            await _database.LogPluginEventAsync(plugin.PluginId, "Info", "Plugin chargé");

            return plugin;
        });

        // Notifier
        if (result.Success && result.PluginId != null)
        {
            PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(result.PluginId, result));
            EventBus.Publish(new PluginLoadedEvent(result.PluginId));
        }
        else
        {
            PluginError?.Invoke(this, new PluginErrorEventArgs(
                pluginPath, result.Exception ?? new Exception("Chargement échoué")));
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  UNLOAD & RELOAD
    // ═══════════════════════════════════════════════════════════════

    public async Task<bool> UnloadPluginAsync(string pluginId)
    {
        LoadedPlugin? loaded;
        lock (_pluginLock)
        {
            if (!_plugins.TryGetValue(pluginId, out loaded))
                return false;
        }

        try
        {
            _logger.LogInformation("Déchargement du plugin '{Plugin}'...", loaded.Plugin.Name);

            // Vérifier les tâches en cours (Quartz)
            if (loaded.Plugin is IScheduledPlugin)
            {
                var runningJobs = await _scheduler.GetCurrentlyExecutingJobs();
                var isRunning = runningJobs.Any(j =>
                    j.JobDetail.Key.Group == "Plugins" && j.JobDetail.Key.Name == pluginId);

                if (isRunning)
                {
                    _logger.LogWarning("Plugin '{Plugin}' a une tâche en cours. Attente...", loaded.Plugin.Name);
                    // Attendre un max de 30s
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(500);
                        runningJobs = await _scheduler.GetCurrentlyExecutingJobs();
                        if (!runningJobs.Any(j => j.JobDetail.Key.Name == pluginId))
                            break;
                    }
                }

                await _scheduler.DeleteJob(new JobKey(pluginId, "Plugins"));
            }

            // Fermer les fenêtres UI
            if (loaded.Plugin is IUIPlugin)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var windows = Application.Current.Windows.OfType<Window>()
                        .Where(w => w.Tag?.ToString() == $"Plugin:{pluginId}")
                        .ToList();
                    foreach (var w in windows)
                        w.Close();
                });
            }

            if (loaded.Plugin is IHttpServicePlugin)
            {
                _httpPlugins.Remove(pluginId);
                await RebuildWebAppAsync();
            }

            // Shutdown du plugin
            try
            {
                await loaded.Plugin.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors du ShutdownAsync de '{Plugin}'", loaded.Plugin.Name);
            }

            // Retirer des plugins actifs
            lock (_pluginLock)
            {
                _plugins.Remove(pluginId);
                _pluginLoggers.Remove(pluginId);
            }

            // Décharger le contexte
            var contextRef = new WeakReference(loaded.Context, trackResurrection: true);
            loaded.Context.Unload();

            // Forcer le GC et attendre le déchargement
            var unloaded = await WaitForUnloadAsync(contextRef);

            // Nettoyer le shadow copy
            loaded.Context.CleanupShadowCopy();

            // Persister en base
            await _database.UnregisterPluginAsync(pluginId);
            await _database.LogPluginEventAsync(pluginId, "Info",
                unloaded ? "Plugin déchargé (mémoire libérée)" : "Plugin déchargé (mémoire en attente de GC)");

            PluginUnloaded?.Invoke(this, new PluginEventArgs(pluginId));
            EventBus.Publish(new PluginUnloadedEvent(pluginId));

            _logger.LogInformation("Plugin '{PluginId}' déchargé (GC complet: {Unloaded})", pluginId, unloaded);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du déchargement de '{PluginId}'", pluginId);
            PluginError?.Invoke(this, new PluginErrorEventArgs(pluginId, ex));
            return false;
        }
    }

    public async Task<PluginLoadResult> ReloadPluginAsync(string pluginId)
    {
        LoadedPlugin? current;
        lock (_pluginLock)
        {
            if (!_plugins.TryGetValue(pluginId, out current))
                return new PluginLoadResult { Success = false, Exception = new KeyNotFoundException($"Plugin '{pluginId}' non trouvé") };
        }

        var pluginPath = current.Metadata.TryGetValue("PluginPath", out var pathObj)
            ? pathObj as string : null;

        if (string.IsNullOrEmpty(pluginPath))
            return new PluginLoadResult { Success = false, Exception = new InvalidOperationException("Chemin du plugin inconnu") };

        var rootDir = current.Metadata.TryGetValue("RootDirectory", out var rootObj)
            ? rootObj as string : null;

        _logger.LogInformation("🔄 Rechargement de '{Plugin}' depuis {Path}...", current.Plugin.Name, pluginPath);

        // Backup
        var backupPath = pluginPath + ".bak";
        try { File.Copy(pluginPath, backupPath, true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Impossible de créer le backup"); }

        // Décharger
        var unloaded = await UnloadPluginAsync(pluginId);
        if (!unloaded)
        {
            _logger.LogError("Échec du déchargement pour rechargement de '{PluginId}'", pluginId);
            return new PluginLoadResult { Success = false, Exception = new InvalidOperationException("Déchargement échoué") };
        }

        // Court délai pour laisser le GC finir
        await Task.Delay(500);

        // Recharger
        var result = await LoadPluginFromFullPathAsync(pluginPath, rootDir);

        if (!result.Success && File.Exists(backupPath))
        {
            _logger.LogWarning("Rechargement échoué, restauration du backup...");
            try
            {
                File.Copy(backupPath, pluginPath, true);
                var fallback = await LoadPluginFromFullPathAsync(pluginPath, rootDir);
                if (fallback.Success)
                    _logger.LogInformation("Restauration réussie avec l'ancienne version");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la restauration du backup");
            }
        }

        // Cleanup backup
        try { if (File.Exists(backupPath)) File.Delete(backupPath); }
        catch { /* best effort */ }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CONFIGURATION DES CAPACITÉS
    // ═══════════════════════════════════════════════════════════════

    private async Task ConfigurePluginCapabilitiesAsync(IPluginBase plugin, LoadedPlugin loaded)
    {
        // ── Quartz ──
        if (plugin is IScheduledPlugin scheduledPlugin)
        {
            try
            {
                var jobKey = new JobKey(plugin.PluginId, "Plugins");
                var job = JobBuilder.Create<PluginJobAdapter>()
                    .WithIdentity(jobKey)
                    .UsingJobData("PluginId", plugin.PluginId)
                    .StoreDurably(scheduledPlugin.IsDurable)
                    .Build();

                // Injecter le manager dans le JobDataMap
                job.JobDataMap["PluginManager"] = this;

                var triggers = scheduledPlugin.GetTriggers();
                foreach (var trigger in triggers)
                {
                    //var jobKey = new JobKey(plugin.PluginId, "Plugins");

                    // Supprimer le job existant si rechargement
                    if (await _scheduler.CheckExists(jobKey))
                        await _scheduler.DeleteJob(jobKey);

                    await _scheduler.ScheduleJob(job, triggers.ToList(), replace: true);
                }

                loaded.Metadata["QuartzJobKey"] = jobKey.ToString();
                loaded.Metadata["TriggerCount"] = triggers.Count;
                _logger.LogInformation("⏱️ Plugin '{Plugin}': {Count} trigger(s) Quartz configuré(s)",
                    plugin.Name, triggers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur configuration Quartz pour '{Plugin}'", plugin.Name);
            }
        }

        // ── HTTP ──
        if (plugin is IHttpServicePlugin httpPlugin)
        {
            try
            {
                await ConfigureHttpAsync(httpPlugin, loaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur configuration HTTP pour '{Plugin}'", plugin.Name);
            }
        }

        // ── Database ──
        if (plugin is IDatabasePlugin dbPlugin)
        {
            try
            {
                await dbPlugin.InitializeDatabaseAsync(_database);
                loaded.Metadata["DatabaseTables"] = dbPlugin.GetRequiredTables();
                loaded.Metadata["SchemaVersion"] = dbPlugin.SchemaVersion;
                _logger.LogInformation("🗄️ Plugin '{Plugin}': schéma DB v{Version} initialisé",
                    plugin.Name, dbPlugin.SchemaVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur configuration DB pour '{Plugin}'", plugin.Name);
            }
        }

        // ── UI ──
        if (plugin is IUIPlugin uiPlugin)
        {
            loaded.Metadata["HasUI"] = true;
            loaded.Metadata["DisplayTitle"] = uiPlugin.DisplayTitle;
            loaded.Metadata["IconPath"] = uiPlugin.IconPath;
            _logger.LogInformation("🖼️ Plugin '{Plugin}': interface UI disponible", plugin.Name);
        }
    }

    private async Task ConfigureHttpAsync(IHttpServicePlugin httpPlugin, LoadedPlugin loaded)
    {
        // Enregistrer le plugin HTTP
        _httpPlugins[httpPlugin.PluginId] = httpPlugin;

        // (Re)construire le serveur web avec tous les plugins HTTP enregistrés
        await RebuildWebAppAsync();

        loaded.Metadata["HttpRoutePrefix"] = httpPlugin.RoutePrefix;
        _logger.LogInformation("🌐 Plugin '{Plugin}': routes HTTP enregistrées sous '{Prefix}'",
            httpPlugin.Name, httpPlugin.RoutePrefix);
    }

    private async Task RebuildWebAppAsync()
    {
        // Stopper l'ancien serveur si existant
        if (_webApp != null)
        {
            try
            {
                await _webApp.StopAsync();
                await _webApp.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de l'arrêt du serveur HTTP précédent");
            }
            _webApp = null;
        }

        if (_httpPlugins.Count == 0)
        {
            _logger.LogInformation("🌐 Plus aucun plugin HTTP, serveur arrêté");
            return;
        }

        // Reconstruire avec tous les plugins
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{_settings.HttpPort}");

        // Enregistrer les services de chaque plugin
        foreach (var plugin in _httpPlugins.Values)
        {
            try
            {
                plugin.ConfigureServices(builder.Services);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur ConfigureServices pour '{Plugin}'", plugin.Name);
            }
        }

        _webApp = builder.Build();

        // Enregistrer les endpoints de chaque plugin
        foreach (var plugin in _httpPlugins.Values)
        {
            try
            {
                plugin.ConfigureEndpoints(_webApp);
                _logger.LogDebug("Routes configurées pour '{Plugin}' sous '{Prefix}'",
                    plugin.Name, plugin.RoutePrefix);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur ConfigureEndpoints pour '{Plugin}'", plugin.Name);
            }
        }

        // Démarrer le serveur
        await _webApp.StartAsync();
        _logger.LogInformation("🌐 Serveur HTTP (re)démarré sur le port {Port} avec {Count} plugin(s)",
            _settings.HttpPort, _httpPlugins.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ACCÈS AUX PLUGINS
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyList<PluginInfo> GetLoadedPlugins()
    {
        lock (_pluginLock)
        {
            return _plugins.Values.Select(p => new PluginInfo
            {
                PluginId = p.Plugin.PluginId,
                Name = p.Plugin.Name,
                Version = p.Plugin.Version,
                InterfaceVersion = p.Plugin.InterfaceVersion,
                Capabilities = p.Plugin.Capabilities,
                LoadTime = p.LoadTimeUtc,
                Warnings = p.LoadResult?.Warnings ?? new(),
                Metadata = new Dictionary<string, object>(p.Metadata)
            }).ToList();
        }
    }

    public T? GetPlugin<T>(string pluginId) where T : class, IPluginBase
    {
        lock (_pluginLock)
        {
            return _plugins.TryGetValue(pluginId, out var loaded)
                ? loaded.Plugin as T
                : null;
        }
    }

    public UIElement? GetPluginUI(string pluginId)
    {
        var plugin = GetPlugin<IUIPlugin>(pluginId);
        if (plugin == null) return null;

        return Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                return plugin.CreateView();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur création UI pour '{PluginId}'", pluginId);
                return null;
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  IPluginHost / IPluginHostV2
    // ═══════════════════════════════════════════════════════════════

    public T? GetService<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
            return (T)service;
        return _serviceProvider.GetService<T>();
    }

    public void RegisterService<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    public async Task<bool> CheckPluginHealthAsync(string pluginId)
    {
        lock (_pluginLock)
        {
            if (!_plugins.ContainsKey(pluginId)) return false;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await Task.Run(() =>
            {
                lock (_pluginLock)
                {
                    return _plugins.TryGetValue(pluginId, out var p) && p.Plugin.PluginId == pluginId;
                }
            }, cts.Token);
            return result;
        }
        catch
        {
            return false;
        }
    }

    public ILogger GetPluginLogger(string pluginId)
    {
        lock (_pluginLock)
        {
            if (_pluginLoggers.TryGetValue(pluginId, out var existing))
                return existing;

            var pluginName = _plugins.TryGetValue(pluginId, out var loaded)
                ? loaded.Plugin.Name
                : pluginId;

            var innerLogger = _loggerFactory.CreateLogger($"Plugin.{pluginId}");
            var bridgeLogger = new PluginBridgeLogger(pluginId, pluginName, innerLogger, EventBus);
            _pluginLoggers[pluginId] = bridgeLogger;
            return bridgeLogger;
        }
    }

    public void Notify(PluginNotification notification)
    {
        _logger.LogInformation("📢 [{Plugin}] {Title}: {Message}",
            notification.PluginId, notification.Title, notification.Message);
        EventBus.Publish(notification);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static Type? FindPluginType(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .FirstOrDefault(t =>
                    typeof(IPluginBase).IsAssignableFrom(t) &&
                    t is { IsAbstract: false, IsInterface: false });
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.FirstOrDefault(t =>
                t != null &&
                typeof(IPluginBase).IsAssignableFrom(t) &&
                t is { IsAbstract: false, IsInterface: false });
        }
    }

    private static async Task<bool> WaitForUnloadAsync(WeakReference contextRef, int maxAttempts = 15)
    {
        for (int i = 0; i < maxAttempts && contextRef.IsAlive; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();
            await Task.Delay(200);
        }
        return !contextRef.IsAlive;
    }

    // ═══════════════════════════════════════════════════════════════
    //  DISPOSE
    // ═══════════════════════════════════════════════════════════════

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Arrêt du PluginManager...");

        _fileWatcher?.Dispose();

        // Décharger tous les plugins
        List<string> pluginIds;
        lock (_pluginLock)
        {
            pluginIds = _plugins.Keys.ToList();
        }

        foreach (var id in pluginIds)
        {
            await UnloadPluginAsync(id);
        }

        // Arrêter Quartz
        if (_scheduler is { IsShutdown: false })
            await _scheduler.Shutdown(waitForJobsToComplete: true);

        // Arrêter le serveur HTTP
        if (_webApp != null)
        {
            await _webApp.StopAsync();
            await _webApp.DisposeAsync();
        }

        _database.Dispose();

        _logger.LogInformation("PluginManager arrêté.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODÈLE INTERNE
    // ═══════════════════════════════════════════════════════════════

    private class LoadedPlugin
    {
        public required IPluginBase Plugin { get; init; }
        public required PluginLoadContext Context { get; init; }
        public required Assembly Assembly { get; init; }
        public DateTime LoadTimeUtc { get; init; }
        public PluginLoadResult? LoadResult { get; set; }
        public Dictionary<string, object?> Metadata { get; } = new();
    }
}

// Événements internes pour l'EventBus
public record PluginLoadedEvent(string PluginId);
public record PluginUnloadedEvent(string PluginId);
