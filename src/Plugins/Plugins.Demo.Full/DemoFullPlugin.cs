using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Quartz;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Plugins;
using PluginFramework.Contracts.Services;
using PluginFramework.Contracts.Versioning;

namespace Plugins.Demo.Full;

[Plugin(Id = "demo-full", Name = "Plugin Complet Démo", Version = "1.0.0", Author = "Benoit",
    Description = "Démontre toutes les capacités: UI + Quartz + DB")]
[InterfaceVersion(2, 0, 0)]
public class DemoFullPlugin : IUIPlugin, IScheduledPlugin, IDatabasePlugin
{
    private IPluginHost _host = null!;
    private IPluginDatabase _db = null!;
    private ILogger _logger = null!;
    private string _lastStatus = "Jamais exécuté";
    private TextBlock? _statusLabel;

    public string PluginId => "demo-full";
    public string Name => "Plugin Complet Démo";
    public string Version => "1.0.0";
    public Version InterfaceVersion => new(2, 0, 0);
    public string DisplayTitle => "🚀 Plugin Complet";
    public int SchemaVersion => 1;
    public bool ShouldRetryOnFailure => true;
    public int MaxRetries => 2;

    public PluginCapabilities Capabilities =>
        PluginCapabilities.UserInterface |
        PluginCapabilities.Scheduled |
        PluginCapabilities.DatabaseAccess;

    // ── Lifecycle ──

    public async Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        _logger = host.GetPluginLogger(PluginId);
        _db = host.GetService<IPluginDatabase>()!;

        var savedStatus = await _db.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM DemoFull_State WHERE Key = 'LastStatus'");
        if (savedStatus != null)
            _lastStatus = savedStatus;

        _logger.LogInformation("DemoFullPlugin initialisé (état restauré: {Status})", _lastStatus);
    }

    public async Task ShutdownAsync()
    {
        // Persister l'état
        await _db.ExecuteAsync(@"
            INSERT OR REPLACE INTO DemoFull_State (Key, Value, UpdatedAt)
            VALUES ('LastStatus', @Value, @UpdatedAt)",
            new { Value = _lastStatus, UpdatedAt = DateTime.UtcNow.ToString("O") });

        _host.Logger.LogInformation("DemoFullPlugin arrêté (état sauvegardé)");
    }

    // ── IDatabasePlugin ──

    public async Task InitializeDatabaseAsync(IPluginDatabase database)
    {
        await database.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DemoFull_State (
                Key         TEXT PRIMARY KEY,
                Value       TEXT,
                UpdatedAt   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS DemoFull_Executions (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ExecutedAt  TEXT NOT NULL,
                Duration    REAL NOT NULL
            );
        ");
    }

    public IReadOnlyList<string> GetRequiredTables() => new[] { "DemoFull_State", "DemoFull_Executions" };

    // ── IScheduledPlugin ──

    public async Task ExecuteAsync(IJobExecutionContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Simuler un travail
        await Task.Delay(200);

        sw.Stop();
        _lastStatus = $"Dernière exécution: {DateTime.Now:HH:mm:ss} ({sw.ElapsedMilliseconds}ms)";

        // Persister l'exécution
        await _db.ExecuteAsync(@"
            INSERT INTO DemoFull_Executions (ExecutedAt, Duration)
            VALUES (@ExecutedAt, @Duration)",
            new { ExecutedAt = DateTime.UtcNow.ToString("O"), Duration = sw.Elapsed.TotalMilliseconds });

        // Mettre à jour l'UI si elle existe
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_statusLabel != null)
                _statusLabel.Text = _lastStatus;
        });

        _host.Logger.LogInformation("🚀 DemoFull exécuté en {Duration}ms", sw.ElapsedMilliseconds);
    }

    public IReadOnlyList<ITrigger> GetTriggers()
    {
        return new[]
        {
            TriggerBuilder.Create()
                .WithIdentity($"{PluginId}.every30s", "Plugins")
                .StartNow()
                .WithSimpleSchedule(s => s.WithIntervalInSeconds(30).RepeatForever())
                .Build()
        };
    }

    // ── IUIPlugin ──

    public UIElement CreateView()
    {
        _statusLabel = new TextBlock
        {
            Text = _lastStatus,
            FontSize = 16,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var historyButton = new Button
        {
            Content = "📊 Voir l'historique des exécutions",
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12, 6, 12, 6)
        };

        var historyPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        historyButton.Click += async (_, _) =>
        {
            try
            {
                var executions = await _db.QueryAsync<ExecutionRecord>(
                    "SELECT ExecutedAt, Duration FROM DemoFull_Executions ORDER BY Id DESC LIMIT 10");

                historyPanel.Children.Clear();
                historyPanel.Children.Add(new TextBlock
                {
                    Text = "Dernières exécutions:",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                foreach (var exec in executions)
                {
                    historyPanel.Children.Add(new TextBlock
                    {
                        Text = $"  • {exec.ExecutedAtParsed:G} — {exec.Duration:F1}ms",
                        FontFamily = new System.Windows.Media.FontFamily("Cascadia Code,Consolas"),
                        FontSize = 12
                    });
                }
            }
            catch (Exception ex)
            {
                _host.Logger.LogError(ex, "Erreur chargement historique");
            }
        };

        return new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "🚀 Plugin Complet — UI + Quartz + SQLite",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold
                },
                new TextBlock
                {
                    Text = "Ce plugin combine interface graphique, tâches planifiées et persistance base de données.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                },
                _statusLabel,
                historyButton,
                historyPanel
            }
        };
    }

    private class ExecutionRecord
    {
        public string ExecutedAt { get; set; } = string.Empty;
        public double Duration { get; set; }

        public DateTime ExecutedAtParsed =>
            DateTime.TryParse(ExecutedAt, out var dt) ? dt : DateTime.MinValue;
    }

}
