using Microsoft.Extensions.Logging;
using PluginFramework.Contracts.Models;
using PluginFramework.Contracts.Services;

namespace PluginFramework.Core.Logging;

/// <summary>
/// Logger qui intercepte les logs d'un plugin et les publie sur l'EventBus
/// pour que le Host puisse les afficher.
/// </summary>
public class PluginBridgeLogger : ILogger
{
    private readonly string _pluginId;
    private readonly string _pluginName;
    private readonly ILogger _innerLogger;
    private readonly IEventAggregator _eventBus;

    public PluginBridgeLogger(string pluginId, string pluginName, ILogger innerLogger, IEventAggregator eventBus)
    {
        _pluginId = pluginId;
        _pluginName = pluginName;
        _innerLogger = innerLogger;
        _eventBus = eventBus;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _innerLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Log normal vers la console/debug
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);

        // Publier sur l'EventBus pour que le Host le capte
        var entry = new PluginLogEntry
        {
            PluginId = _pluginId,
            PluginName = _pluginName,
            Level = logLevel.ToString(),
            Message = formatter(state, exception),
            Exception = exception?.ToString(),
            Timestamp = DateTime.Now
        };

        try
        {
            _eventBus.Publish(entry);
        }
        catch
        {
            // Ne jamais crasher à cause du logging
        }
    }
}
