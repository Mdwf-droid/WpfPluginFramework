namespace PluginFramework.Contracts.Base;

[Flags]
public enum PluginCapabilities
{
    None = 0,
    UserInterface = 1 << 0,
    Scheduled = 1 << 1,
    HttpService = 1 << 2,
    DatabaseAccess = 1 << 3,
    BackgroundTask = 1 << 4
}
