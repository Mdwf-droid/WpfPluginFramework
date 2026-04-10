namespace PluginFramework.Configuration;

public class PluginSettings
{
    /// <summary>Liste des répertoires de plugins</summary>
    public List<PluginDirectoryConfig> Directories { get; set; } = new();

    /// <summary>Pattern global de recherche des DLLs</summary>
    public string SearchPattern { get; set; } = "*.Plugin.dll";

    /// <summary>Profondeur max de récursion</summary>
    public int MaxRecursionDepth { get; set; } = 10;

    /// <summary>Ignorer les dossiers cachés (attribut Hidden)</summary>
    public bool IgnoreHiddenDirectories { get; set; } = true;

    /// <summary>Noms de dossiers à ignorer</summary>
    public List<string> IgnoreDirectories { get; set; } = new() { ".git", "bin", "obj", "node_modules", ".vs" };

    /// <summary>Dossier pour les données internes (SQLite, logs...)</summary>
    public string? DataDirectory { get; set; }

    /// <summary>Port HTTP pour les plugins exposant des endpoints</summary>
    public int HttpPort { get; set; } = 5100;

    /// <summary>Timeout de chargement d'un plugin (secondes)</summary>
    public int LoadTimeoutSeconds { get; set; } = 30;

    /// <summary>Activer la surveillance des fichiers (hot reload)</summary>
    public bool EnableFileWatching { get; set; } = true;

    /// <summary>Délai debounce pour le file watcher (ms)</summary>
    public int FileWatcherDebounceMs { get; set; } = 2000;

    /// <summary>Nombre max d'échecs avant circuit breaker</summary>
    public int CircuitBreakerMaxFailures { get; set; } = 3;

    /// <summary>Durée d'ouverture du circuit breaker (minutes)</summary>
    public int CircuitBreakerCooldownMinutes { get; set; } = 5;
}
