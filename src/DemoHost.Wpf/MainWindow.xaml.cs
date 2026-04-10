using PluginFramework.Core;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DemoHost.Wpf;

public partial class MainWindow : Window
{
    private AdvancedPluginManager? Manager => DesignerProperties.GetIsInDesignMode(this) ? null : ((App)Application.Current).PluginManager;

    // Tracker les onglets plugin par ID
    private readonly Dictionary<string, TabItem> _pluginTabs = new();

    public MainWindow()
    {
        InitializeComponent();

        if (DesignerProperties.GetIsInDesignMode(this))
            return;

        Manager!.PluginUnloaded += (_, args) =>
        {
            Dispatcher.Invoke(() => ClosePluginUI(args.PluginId));
        };

        Manager!.PluginLoaded += (_, args) =>
        {
            Dispatcher.Invoke(() => ClosePluginUI(args.PluginId));
        };
    }


    public void ShowPluginUI(string pluginId)
    {
        // Si déjà ouvert, juste basculer dessus
        if (_pluginTabs.TryGetValue(pluginId, out var existingTab))
        {
            MainTabControl.SelectedItem = existingTab;
            return;
        }

        var plugins = Manager.GetLoadedPlugins();
        var pluginInfo = plugins.FirstOrDefault(p => p.PluginId == pluginId);
        if (pluginInfo is not { HasUI: true }) return;

        var uiElement = Manager.GetPluginUI(pluginId);
        if (uiElement == null) return;

        var tab = new TabItem
        {
            Tag = pluginId,
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = pluginInfo.Name,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    },
                    new Button
                    {
                        Content = "✕",
                        FontSize = 10,
                        Padding = new Thickness(4, 0, 4, 0),
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Tag = pluginId
                    }
                }
            },
            Content = uiElement
        };

        // Bouton fermer l'onglet (sans décharger le plugin)
        var closeBtn = ((StackPanel)tab.Header).Children[1] as Button;
        closeBtn!.Click += (_, _) => ClosePluginUI(pluginId);

        _pluginTabs[pluginId] = tab;
        MainTabControl.Items.Add(tab);
        MainTabControl.SelectedItem = tab;
    }

    private void ClosePluginUI(string pluginId)
    {
        if (!_pluginTabs.TryGetValue(pluginId, out var tab))
            return;

        // Détacher le contenu avant de retirer l'onglet
        tab.Content = null;

        MainTabControl.Items.Remove(tab);
        _pluginTabs.Remove(pluginId);

        // Revenir à l'onglet Manager si plus rien
        if (MainTabControl.Items.Count > 0)
            MainTabControl.SelectedIndex = 0;
    }

    protected override async void OnClosed(EventArgs e)
    {
        // Fermer tous les onglets plugin proprement
        foreach (var pluginId in _pluginTabs.Keys.ToList())
            ClosePluginUI(pluginId);

        await Manager.DisposeAsync();
        base.OnClosed(e);
    }
}