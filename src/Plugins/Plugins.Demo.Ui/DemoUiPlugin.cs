using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Plugins;
using PluginFramework.Contracts.Versioning;

namespace Plugins.Demo.Ui;

[Plugin(Id = "demo-ui", Name = "Plugin UI Démo", Version = "1.0.0", Author = "Benoit")]
[InterfaceVersion(2, 0, 0)]
public class DemoUiPlugin : IUIPlugin
{
    private IPluginHost _host = null!;
    private int _clickCount;

    public string PluginId => "demo-ui";
    public string Name => "Plugin UI Démo";
    public string Version => "1.0.0";
    public Version InterfaceVersion => new(2, 0, 0);
    public PluginCapabilities Capabilities => PluginCapabilities.UserInterface;
    public string DisplayTitle => "🎨 Démo UI";

    public Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        _host.Logger.LogInformation("DemoUiPlugin initialisé");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _host.Logger.LogInformation("DemoUiPlugin arrêté (clicks: {Count})", _clickCount);
        return Task.CompletedTask;
    }

    public UIElement CreateView()
    {
        var countLabel = new TextBlock
        {
            Text = $"Clicks: {_clickCount}",
            FontSize = 18,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var button = new Button
        {
            Content = "🖱️ Cliquez-moi !",
            FontSize = 16,
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(0, 10, 0, 0)
        };

        button.Click += (_, _) =>
        {
            _clickCount++;
            countLabel.Text = $"Clicks: {_clickCount}";
            _host.Logger.LogInformation("Click #{Count}", _clickCount);
        };

        return new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "🎨 Plugin UI de Démonstration",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46))
                },
                new TextBlock
                {
                    Text = "Ce plugin démontre l'intégration d'une UI WPF dans l'application hôte.",
                    FontSize = 14,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                },
                button,
                countLabel
            }
        };
    }
}
