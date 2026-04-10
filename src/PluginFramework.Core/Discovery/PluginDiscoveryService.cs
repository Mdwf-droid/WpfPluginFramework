using Microsoft.Extensions.Logging;
using PluginFramework.Configuration;
using System.IO;

namespace PluginFramework.Core.Discovery;

public class PluginDiscoveryService
{
    private readonly PluginSettings _settings;
    private readonly ILogger _logger;

    public PluginDiscoveryService(PluginSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public IReadOnlyList<DiscoveredPluginFile> DiscoverAll()
    {
        var results = new List<DiscoveredPluginFile>();

        var sortedDirs = _settings.Directories.OrderBy(d => d.Priority).ToList();

        foreach (var dirConfig in sortedDirs)
        {
            try
            {
                ScanDirectory(dirConfig, results);
            }
            catch (Exception ex)
            {
                if (!dirConfig.Optional)
                    _logger.LogError(ex, "Erreur critique sur répertoire obligatoire: {Path}", dirConfig.Path);
                else
                    _logger.LogWarning(ex, "Erreur sur répertoire optionnel: {Path}", dirConfig.Path);
            }
        }

        // Dédoublonner par chemin complet
        var deduplicated = results
            .GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation("Découverte terminée: {Count} plugin(s) candidat(s) trouvé(s) dans {DirCount} répertoire(s)",
            deduplicated.Count, sortedDirs.Count);

        return deduplicated;
    }

    private void ScanDirectory(PluginDirectoryConfig dirConfig, List<DiscoveredPluginFile> results)
    {
        var root = dirConfig.Path;
        if (!Path.IsPathRooted(root))
            root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, root));

        if (!Directory.Exists(root))
        {
            var level = dirConfig.Optional ? LogLevel.Information : LogLevel.Warning;
            _logger.Log(level, "Répertoire {Status}: {Path}",
                dirConfig.Optional ? "optionnel introuvable" : "OBLIGATOIRE introuvable", root);
            return;
        }

        var searchPattern = dirConfig.SearchPatternOverride ?? _settings.SearchPattern;
        _logger.LogDebug("Scan: {Root} (pattern={Pattern}, recursive={Recursive})",
            root, searchPattern, dirConfig.Recursive);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (currentDir, depth) = stack.Pop();

            // Anti-boucle (symlinks circulaires)
            var realPath = Path.GetFullPath(currentDir);
            if (!visited.Add(realPath))
                continue;

            try
            {
                // Vérification attributs du dossier
                if (ShouldSkipDirectory(currentDir))
                    continue;

                // Énumération des fichiers plugins
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir, searchPattern, SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("Accès refusé: {Dir} ({Message})", currentDir, ex.Message);
                    continue;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning("Erreur I/O: {Dir} ({Message})", currentDir, ex.Message);
                    continue;
                }

                foreach (var filePath in files)
                {
                    try
                    {
                        var info = new FileInfo(filePath);
                        if (info.Length == 0)
                        {
                            _logger.LogDebug("Fichier vide ignoré: {File}", filePath);
                            continue;
                        }

                        results.Add(new DiscoveredPluginFile
                        {
                            FullPath = filePath,
                            DirectoryRoot = root,
                            RelativePath = Path.GetRelativePath(root, filePath),
                            FileSize = info.Length,
                            LastModifiedUtc = info.LastWriteTimeUtc
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Erreur sur fichier: {File}", filePath);
                    }
                }

                // Récursion dans les sous-dossiers
                if (dirConfig.Recursive && depth < _settings.MaxRecursionDepth)
                {
                    IEnumerable<string> subDirs;
                    try
                    {
                        subDirs = Directory.EnumerateDirectories(currentDir);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _logger.LogWarning("Accès refusé aux sous-dossiers de: {Dir}", currentDir);
                        continue;
                    }

                    foreach (var sub in subDirs)
                        stack.Push((sub, depth + 1));
                }
                else if (depth >= _settings.MaxRecursionDepth)
                {
                    _logger.LogDebug("Profondeur max atteinte: {Dir}", currentDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur exploration: {Dir}", currentDir);
            }
        }
    }

    private bool ShouldSkipDirectory(string dirPath)
    {
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);

            // Ignorer les dossiers cachés / système
            if (_settings.IgnoreHiddenDirectories &&
                (dirInfo.Attributes.HasFlag(FileAttributes.Hidden) ||
                 dirInfo.Attributes.HasFlag(FileAttributes.System)))
            {
                _logger.LogDebug("Dossier caché/système ignoré: {Dir}", dirPath);
                return true;
            }

            // Ignorer les dossiers dans la liste d'exclusion
            var dirName = dirInfo.Name;
            if (_settings.IgnoreDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Dossier ignoré (liste d'exclusion): {Dir}", dirPath);
                return true;
            }
        }
        catch
        {
            // En cas d'erreur sur les attributs, on continue
        }

        return false;
    }
}
