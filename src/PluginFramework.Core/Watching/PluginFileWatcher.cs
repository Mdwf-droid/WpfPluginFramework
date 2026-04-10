using Microsoft.Extensions.Logging;
using PluginFramework.Configuration;
using PluginFramework.Contracts.Models;
using PluginFramework.Core.Discovery;
using System.IO;

namespace PluginFramework.Core.Watching;

public class PluginFileWatcher : IDisposable
{
    private readonly AdvancedPluginManager _pluginManager;
    private readonly PluginSettings _settings;
    private readonly ILogger _logger;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Debouncer> _debouncers = new();

    public PluginFileWatcher(AdvancedPluginManager pluginManager, PluginSettings settings, ILogger logger)
    {
        _pluginManager = pluginManager;
        _settings = settings;
        _logger = logger;
    }

    public void StartWatching(IEnumerable<PluginDirectoryConfig> directories)
    {
        foreach (var dirConfig in directories)
        {
            var root = dirConfig.Path;
            if (!Path.IsPathRooted(root))
                root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, root));

            if (!Directory.Exists(root))
                continue;

            if (_watchers.ContainsKey(root))
                continue;

            try
            {
                var pattern = dirConfig.SearchPatternOverride ?? _settings.SearchPattern;

                var watcher = new FileSystemWatcher(root)
                {
                    Filter = pattern,
                    IncludeSubdirectories = dirConfig.Recursive,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                var debouncer = new Debouncer(TimeSpan.FromMilliseconds(_settings.FileWatcherDebounceMs));

                watcher.Changed += (s, e) => OnFileEvent(e.FullPath, e.ChangeType, debouncer);
                watcher.Created += (s, e) => OnFileEvent(e.FullPath, e.ChangeType, debouncer);
                watcher.Deleted += (s, e) => OnFileEvent(e.FullPath, e.ChangeType, debouncer);
                watcher.Renamed += (s, e) =>
                {
                    OnFileEvent(e.OldFullPath, WatcherChangeTypes.Deleted, debouncer);
                    OnFileEvent(e.FullPath, WatcherChangeTypes.Created, debouncer);
                };

                watcher.Error += (s, e) =>
                    _logger.LogError(e.GetException(), "Erreur FileSystemWatcher sur {Root}", root);

                _watchers[root] = watcher;
                _debouncers[root] = debouncer;
                _logger.LogInformation("👁️ Surveillance activée: {Root} (pattern={Pattern}, recursive={Recursive})",
                    root, pattern, dirConfig.Recursive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Impossible de surveiller: {Root}", root);
            }
        }
    }

    private async void OnFileEvent(string filePath, WatcherChangeTypes changeType, Debouncer debouncer)
    {
        await debouncer.DebounceAsync(async () =>
        {
            try
            {
                _logger.LogInformation("📁 Événement fichier: {ChangeType} → {File}", changeType, filePath);

                // Trouver le plugin associé
                var existingPlugin = _pluginManager.GetLoadedPlugins()
                    .FirstOrDefault(p =>
                        p.Metadata.TryGetValue("PluginPath", out var path) &&
                        string.Equals(path as string, filePath, StringComparison.OrdinalIgnoreCase));

                switch (changeType)
                {
                    case WatcherChangeTypes.Created:
                        if (existingPlugin == null)
                        {
                            _logger.LogInformation("🆕 Nouveau plugin détecté, chargement...");
                            // Attendre que le fichier soit complètement écrit
                            await WaitForFileReady(filePath);
                            var loadResult = await _pluginManager.LoadPluginFromFullPathAsync(filePath);
                            LogResult("Chargement", filePath, loadResult);
                        }
                        break;

                    case WatcherChangeTypes.Changed:
                        if (existingPlugin != null)
                        {
                            _logger.LogInformation("🔄 Modification détectée, rechargement de '{Plugin}'...", existingPlugin.Name);
                            await WaitForFileReady(filePath);
                            var reloadResult = await _pluginManager.ReloadPluginAsync(existingPlugin.PluginId);
                            LogResult("Rechargement", filePath, reloadResult);
                        }
                        break;

                    case WatcherChangeTypes.Deleted:
                        if (existingPlugin != null)
                        {
                            _logger.LogInformation("🗑️ Suppression détectée, déchargement de '{Plugin}'...", existingPlugin.Name);
                            await _pluginManager.UnloadPluginAsync(existingPlugin.PluginId);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur traitement événement fichier: {File}", filePath);
            }
        });
    }

    private void LogResult(string action, string filePath, PluginLoadResult result)
    {
        if (result.Success)
            _logger.LogInformation("✅ {Action} réussi: {File} → {PluginId}", action, filePath, result.PluginId);
        else
            _logger.LogWarning(result.Exception, "❌ {Action} échoué: {File}", action, filePath);
    }

    private static async Task WaitForFileReady(string filePath, int maxAttempts = 10)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return; // Fichier prêt
            }
            catch (IOException)
            {
                await Task.Delay(500);
            }
        }
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        foreach (var debouncer in _debouncers.Values) debouncer.Dispose();
        _watchers.Clear();
        _debouncers.Clear();
    }

    public void Dispose() => StopWatching();
}
