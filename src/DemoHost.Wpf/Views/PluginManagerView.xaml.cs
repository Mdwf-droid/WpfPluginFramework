using Microsoft.Win32;
using PluginFramework.Contracts.Models;
using PluginFramework.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DemoHost.Wpf.Views;

public partial class PluginManagerView : UserControl
{
    private AdvancedPluginManager? Manager =>
    DesignerProperties.GetIsInDesignMode(this) ? null : ((App)Application.Current).PluginManager;

    private readonly ObservableCollection<LogEntry> _allLogs = new();
    private readonly ObservableCollection<LogEntry> _filteredLogs = new();
    private IDisposable? _logSubscription;
    private IDisposable? _notifSubscription;

    public PluginManagerView()
    {
        InitializeComponent();
        LogList.ItemsSource = _filteredLogs;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this) || Manager == null)
            return;
        // Événements du manager
        Manager.PluginLoaded += (_, args) => Dispatcher.Invoke(() =>
        {
            RefreshList();
            AddLog("System", "Host", $"✅ Plugin chargé: {args.PluginId}", "Info");
            UpdatePluginFilterCombo();
        });

        Manager.PluginUnloaded += (_, args) => Dispatcher.Invoke(() =>
        {
            RefreshList();
            AddLog("System", "Host", $"🔻 Plugin déchargé: {args.PluginId}", "Info");
            UpdatePluginFilterCombo();
        });

        Manager.PluginError += (_, args) => Dispatcher.Invoke(() =>
        {
            AddLog("System", "Host", $"❌ Erreur [{args.PluginId}]: {args.Exception.Message}", "Error");
        });

        // S'abonner aux logs des plugins via l'EventBus
        _logSubscription = Manager.EventBus.Subscribe<PluginLogEntry>(entry =>
        {
            Dispatcher.Invoke(() =>
            {
                AddLog(entry.PluginId, entry.PluginName, entry.Message, entry.Level);
            });
        });

        // S'abonner aux notifications des plugins
        _notifSubscription = Manager.EventBus.Subscribe<PluginNotification>(notif =>
        {
            Dispatcher.Invoke(() =>
            {
                ShowNotification(notif);
                AddLog(notif.PluginId, notif.PluginId,
                    $"📢 [{notif.Title}] {notif.Message}", notif.Level.ToString());
            });
        });

        RefreshList();
        UpdatePluginFilterCombo();
        AddLog("System", "Host", "🚀 Host démarré, surveillance des plugins active", "Info");
    }

    // ═══════════════════════════════════════════════════════
    //  LOGS
    // ═══════════════════════════════════════════════════════

    private void AddLog(string pluginId, string pluginName, string message, string level)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            PluginId = pluginId,
            PluginName = pluginName,
            Message = message,
            Level = level
        };

        _allLogs.Add(entry);

        // Limiter à 500 entrées
        while (_allLogs.Count > 500)
            _allLogs.RemoveAt(0);

        // Appliquer le filtre
        if (MatchesFilter(entry))
        {
            _filteredLogs.Add(entry);
            while (_filteredLogs.Count > 500)
                _filteredLogs.RemoveAt(0);
        }

        LogScroller.ScrollToEnd();
    }

    private bool MatchesFilter(LogEntry entry)
    {
        // Filtre par catégorie
        var categoryIndex = LogFilterCombo.SelectedIndex;
        switch (categoryIndex)
        {
            case 1: // Plugins seuls
                if (entry.PluginId == "System") return false;
                break;
            case 2: // Erreurs
                if (entry.Level != "Error" && entry.Level != "Warning" && entry.Level != "Critical")
                    return false;
                break;
            case 3: // Notifications
                if (!entry.Message.StartsWith("📢")) return false;
                break;
        }

        // Filtre par plugin
        var pluginFilter = (PluginFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (pluginFilter != null && pluginFilter != "Tous" && entry.PluginName != pluginFilter)
            return false;

        return true;
    }

    private void LogFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filteredLogs.Clear();
        foreach (var entry in _allLogs)
        {
            if (MatchesFilter(entry))
                _filteredLogs.Add(entry);
        }
        LogScroller?.ScrollToEnd();
    }

    private void UpdatePluginFilterCombo()
    {
        var selected = (PluginFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        PluginFilterCombo.Items.Clear();
        PluginFilterCombo.Items.Add(new ComboBoxItem { Content = "Tous", IsSelected = true });

        foreach (var plugin in Manager.GetLoadedPlugins())
        {
            var item = new ComboBoxItem { Content = plugin.Name };
            if (plugin.Name == selected) item.IsSelected = true;
            PluginFilterCombo.Items.Add(item);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  NOTIFICATIONS
    // ═══════════════════════════════════════════════════════

    private void ShowNotification(PluginNotification notif)
    {
        NotificationBanner.Background = notif.Level switch
        {
            NotificationLevel.Success => new SolidColorBrush(Color.FromRgb(212, 237, 218)),
            NotificationLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 243, 205)),
            NotificationLevel.Error => new SolidColorBrush(Color.FromRgb(248, 215, 218)),
            _ => new SolidColorBrush(Color.FromRgb(209, 236, 241))
        };

        var icon = notif.Level switch
        {
            NotificationLevel.Success => "✅",
            NotificationLevel.Warning => "⚠️",
            NotificationLevel.Error => "❌",
            _ => "ℹ️"
        };

        NotificationText.Text = $"{icon} [{notif.PluginId}] {notif.Title}" +
                                 (string.IsNullOrEmpty(notif.Message) ? "" : $"\n{notif.Message}");
        NotificationBanner.Visibility = Visibility.Visible;

        // Auto-dismiss après 10s pour info/success
        if (notif.Level is NotificationLevel.Info or NotificationLevel.Success)
        {
            _ = Task.Delay(10000).ContinueWith(_ =>
                Dispatcher.Invoke(() => NotificationBanner.Visibility = Visibility.Collapsed));
        }
    }

    private void DismissNotification_Click(object sender, RoutedEventArgs e)
    {
        NotificationBanner.Visibility = Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════════════════
    //  ACTIONS TOOLBAR
    // ═══════════════════════════════════════════════════════

    private void RefreshList()
    {
        var plugins = Manager.GetLoadedPlugins();
        PluginGrid.ItemsSource = plugins;
        StatusText.Text = $"{plugins.Count} plugin(s) chargé(s)";
    }

    private async void LoadPlugin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Plugin (*.Plugin.dll)|*.Plugin.dll|Tous (*.dll)|*.dll",
            InitialDirectory = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Plugins")
        };

        if (dialog.ShowDialog() != true) return;

        AddLog("System", "Host",
            $"⏳ Chargement de {System.IO.Path.GetFileName(dialog.FileName)}...", "Info");
        var result = await Manager.LoadPluginFromFullPathAsync(dialog.FileName);

        if (!result.Success)
            AddLog("System", "Host", $"❌ Échec: {result.Exception?.Message}", "Error");

        foreach (var w in result.Warnings)
            AddLog("System", "Host", $"⚠️ {w}", "Warning");
    }

    private async void ReloadPlugin_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPlugin();
        if (selected == null) return;

        AddLog("System", "Host", $"🔄 Rechargement de {selected.Name}...", "Info");
        var result = await Manager.ReloadPluginAsync(selected.PluginId);

        if (!result.Success)
            AddLog("System", "Host", $"❌ Échec rechargement: {result.Exception?.Message}", "Error");
    }

    private async void UnloadPlugin_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPlugin();
        if (selected == null) return;

        AddLog("System", "Host", $"🔻 Déchargement de {selected.Name}...", "Info");
        await Manager.UnloadPluginAsync(selected.PluginId);
    }

    private void ShowUI_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPlugin();
        if (selected is not { HasUI: true })
        {
            AddLog("System", "Host", "⚠️ Ce plugin n'expose pas d'interface utilisateur", "Warning");
            return;
        }

        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.ShowPluginUI(selected.PluginId);
            AddLog("System", "Host", $"🖼️ Affichage UI de {selected.Name}", "Info");
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _allLogs.Clear();
        _filteredLogs.Clear();
    }

    private PluginInfo? SelectedPlugin() => PluginGrid.SelectedItem as PluginInfo;

    // ═══════════════════════════════════════════════════════
    //  MODÈLE LOG
    // ═══════════════════════════════════════════════════════

    public class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public string PluginId { get; init; } = "";
        public string PluginName { get; init; } = "";
        public string Message { get; init; } = "";
        public string Level { get; init; } = "Info";

        public string Display => $"[{Timestamp:HH:mm:ss}] [{PluginName}] {Message}";

        public Brush Color => Level switch
        {
            "Error" or "Critical" => Brushes.Red,
            "Warning" => Brushes.DarkOrange,
            "Debug" or "Trace" => Brushes.Gray,
            _ when PluginId == "System" => Brushes.SteelBlue,
            _ => Brushes.DarkGreen
        };
    }
}
