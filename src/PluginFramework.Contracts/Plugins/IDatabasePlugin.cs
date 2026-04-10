using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Services;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Plugins;

[InterfaceVersion(1, 0, 0)]
public interface IDatabasePlugin : IPluginBase
{
    /// <summary>Initialisation du schéma DB du plugin</summary>
    Task InitializeDatabaseAsync(IPluginDatabase database);

    /// <summary>Tables requises par le plugin</summary>
    IReadOnlyList<string> GetRequiredTables();

    /// <summary>Version du schéma pour gérer les migrations</summary>
    int SchemaVersion => 1;
}
