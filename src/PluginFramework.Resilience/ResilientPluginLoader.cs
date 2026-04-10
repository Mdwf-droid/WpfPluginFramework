using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PluginFramework.Configuration;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Models;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Resilience;

public class ResilientPluginLoader
{
    private readonly ILogger _logger;
    private readonly PluginSettings _settings;
    private readonly InterfaceCompatibilityChecker _compatibilityChecker = new();
    private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();

    public ResilientPluginLoader(ILogger logger, PluginSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public async Task<PluginLoadResult> LoadPluginSafelyAsync(
        string pluginPath,
        Func<string, Task<IPluginBase>> loadFunc)
    {
        var result = new PluginLoadResult { PluginPath = pluginPath };
        var pluginFileName = Path.GetFileNameWithoutExtension(pluginPath);
        var sw = Stopwatch.StartNew();

        try
        {
            // ── Circuit breaker ──
            if (IsCircuitOpen(pluginFileName))
            {
                var state = _circuitBreakers[pluginFileName];
                result.Exception = new InvalidOperationException(
                    $"Circuit breaker ouvert pour '{pluginFileName}' ({state.FailureCount} échecs). " +
                    $"Réessai possible après {state.LastFailure.AddMinutes(_settings.CircuitBreakerCooldownMinutes):HH:mm:ss}");
                _logger.LogWarning(result.Exception.Message);
                return result;
            }

            // ── Validation fichier ──
            if (!ValidateFile(pluginPath, result))
                return result;

            _logger.LogInformation("⏳ Chargement de '{Plugin}'...", pluginFileName);

            // ── Chargement avec timeout ──
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.LoadTimeoutSeconds));

            IPluginBase plugin;
            try
            {
                plugin = await Task.Run(() => loadFunc(pluginPath), cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Timeout ({_settings.LoadTimeoutSeconds}s) lors du chargement de '{pluginFileName}'");
            }

            // ── Compatibilité d'interface ──
            var compatibility = _compatibilityChecker.Check(typeof(IPluginBase), plugin.GetType());
            result.Warnings.AddRange(compatibility.Warnings);

            if (!compatibility.IsCompatible)
            {
                result.Exception = new InvalidOperationException(
                    $"Interface incompatible: {string.Join("; ", compatibility.Errors)}");
                _logger.LogError(result.Exception.Message);
                RecordFailure(pluginFileName, result.Exception);
                return result;
            }

            foreach (var warning in compatibility.Warnings)
                _logger.LogWarning("⚠️ {Warning}", warning);

            // ── Succès ──
            result.Success = true;
            result.PluginId = plugin.PluginId;
            result.Metadata["InterfaceVersion"] = plugin.InterfaceVersion?.ToString() ?? "unknown";
            result.Metadata["Capabilities"] = plugin.Capabilities;

            ResetCircuitBreaker(pluginFileName);

            sw.Stop();
            result.LoadDuration = sw.Elapsed;
            _logger.LogInformation("✅ Plugin '{Plugin}' chargé en {Duration}ms",
                plugin.Name, sw.ElapsedMilliseconds);
        }
        catch (TimeoutException ex)
        {
            result.Exception = ex;
            _logger.LogError("⏰ {Message}", ex.Message);
            RecordFailure(pluginFileName, ex);
        }
        catch (ReflectionTypeLoadException ex)
        {
            result.Exception = ex;
            _logger.LogError("❌ Erreur de chargement de types pour '{Plugin}'", pluginFileName);
            foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null).Take(5))
                _logger.LogError("   ↳ {Message}", loaderEx!.Message);
            RecordFailure(pluginFileName, ex);
        }
        catch (FileLoadException ex)
        {
            result.Exception = ex;
            _logger.LogError(ex, "❌ Erreur de chargement fichier '{Plugin}'", pluginFileName);
            RecordFailure(pluginFileName, ex);
        }
        catch (BadImageFormatException ex)
        {
            result.Exception = ex;
            _logger.LogError("❌ Fichier '{Plugin}' n'est pas un assembly .NET valide", pluginFileName);
            RecordFailure(pluginFileName, ex);
        }
        catch (Exception ex)
        {
            result.Exception = ex;
            _logger.LogError(ex, "❌ Erreur inattendue lors du chargement de '{Plugin}'", pluginFileName);
            RecordFailure(pluginFileName, ex);
        }

        return result;
    }

    private bool ValidateFile(string path, PluginLoadResult result)
    {
        if (!File.Exists(path))
        {
            result.Exception = new FileNotFoundException($"Fichier introuvable: {path}");
            _logger.LogError("❌ {Message}", result.Exception.Message);
            return false;
        }

        var info = new FileInfo(path);

        if (info.Length == 0)
        {
            result.Exception = new InvalidOperationException($"Fichier vide: {path}");
            _logger.LogWarning("⚠️ {Message}", result.Exception.Message);
            return false;
        }

        result.Metadata["FileSize"] = info.Length;
        result.Metadata["LastModified"] = info.LastWriteTimeUtc;
        return true;
    }

    private bool IsCircuitOpen(string pluginName)
    {
        if (!_circuitBreakers.TryGetValue(pluginName, out var state))
            return false;

        if (state.FailureCount >= _settings.CircuitBreakerMaxFailures)
        {
            if (DateTime.UtcNow - state.LastFailure < TimeSpan.FromMinutes(_settings.CircuitBreakerCooldownMinutes))
                return true;

            // Cooldown terminé → reset
            _circuitBreakers.Remove(pluginName);
        }

        return false;
    }

    private void RecordFailure(string pluginName, Exception ex)
    {
        if (!_circuitBreakers.ContainsKey(pluginName))
            _circuitBreakers[pluginName] = new CircuitBreakerState();

        _circuitBreakers[pluginName].RecordFailure(ex);
    }

    private void ResetCircuitBreaker(string pluginName)
    {
        _circuitBreakers.Remove(pluginName);
    }
}
