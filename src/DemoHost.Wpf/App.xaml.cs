using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PluginFramework.Configuration;
using PluginFramework.Core;

namespace DemoHost.Wpf;

public partial class App : Application
{
    internal AdvancedPluginManager PluginManager { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var settings = new PluginSettings();
        configuration.GetSection("PluginSettings").Bind(settings);

        // PluginManager
        PluginManager = new AdvancedPluginManager(settings, configuration);

        // Découverte et chargement initial
        var results = await PluginManager.DiscoverAndLoadAllAsync();

        foreach (var res in results)
        {
            if (!res.Success)
                PluginManager.Logger.LogWarning(res.Exception, "Échec chargement: {Path}", res.PluginPath);
        }

        // Fenêtre principale
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (PluginManager != null)
            await PluginManager.DisposeAsync();
        base.OnExit(e);
    }
}
