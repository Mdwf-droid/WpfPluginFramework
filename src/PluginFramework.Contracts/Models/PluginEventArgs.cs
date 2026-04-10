namespace PluginFramework.Contracts.Models;

public class PluginEventArgs : EventArgs
{
    public string PluginId { get; }
    public PluginEventArgs(string pluginId) => PluginId = pluginId;
}

public class PluginLoadedEventArgs : PluginEventArgs
{
    public PluginLoadResult LoadResult { get; }
    public PluginLoadedEventArgs(string pluginId, PluginLoadResult result) : base(pluginId) => LoadResult = result;
}

public class PluginErrorEventArgs : PluginEventArgs
{
    public Exception Exception { get; }
    public PluginErrorEventArgs(string pluginId, Exception exception) : base(pluginId) => Exception = exception;
}
