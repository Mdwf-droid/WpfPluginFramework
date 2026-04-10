using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PluginFramework.Contracts.Services;

namespace PluginFramework.Services.Database;

public class SharedDatabaseService : IPluginDatabase, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger _logger;

    public SharedDatabaseService(string dbPath, ILogger logger)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        InitializeCoreTables();
    }

    private void InitializeCoreTables()
    {
        using var connection = CreateConnection();
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS __PluginRegistry (
                PluginId    TEXT PRIMARY KEY,
                Name        TEXT NOT NULL,
                Version     TEXT NOT NULL,
                LoadedAt    TEXT NOT NULL,
                UnloadedAt  TEXT,
                Status      TEXT NOT NULL DEFAULT 'Active'
            );

            CREATE TABLE IF NOT EXISTS __PluginLogs (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                PluginId    TEXT NOT NULL,
                Timestamp   TEXT NOT NULL,
                Level       TEXT NOT NULL,
                Message     TEXT NOT NULL,
                Exception   TEXT,
                FOREIGN KEY (PluginId) REFERENCES __PluginRegistry(PluginId)
            );

            CREATE TABLE IF NOT EXISTS __PluginState (
                PluginId    TEXT NOT NULL,
                Key         TEXT NOT NULL,
                Value       TEXT,
                UpdatedAt   TEXT NOT NULL,
                PRIMARY KEY (PluginId, Key)
            );

            CREATE INDEX IF NOT EXISTS idx_logs_plugin_ts
                ON __PluginLogs(PluginId, Timestamp DESC);
        ");
        _logger.LogInformation("Base de données plugins initialisée");
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            return await conn.ExecuteAsync(sql, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur SQL Execute: {Sql}", Truncate(sql));
            throw;
        }
        finally { _semaphore.Release(); }
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<T>(sql, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur SQL Query: {Sql}", Truncate(sql));
            throw;
        }
        finally { _semaphore.Release(); }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<T>(sql, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur SQL Query: {Sql}", Truncate(sql));
            throw;
        }
        finally { _semaphore.Release(); }
    }

    public IDbConnection GetConnection() => CreateConnection();

    // ── Méthodes internes pour le framework ──

    public async Task RegisterPluginAsync(string pluginId, string name, string version)
    {
        await ExecuteAsync(@"
            INSERT OR REPLACE INTO __PluginRegistry (PluginId, Name, Version, LoadedAt, Status)
            VALUES (@PluginId, @Name, @Version, @LoadedAt, 'Active')",
            new { PluginId = pluginId, Name = name, Version = version, LoadedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task UnregisterPluginAsync(string pluginId)
    {
        await ExecuteAsync(
            "UPDATE __PluginRegistry SET UnloadedAt = @Now, Status = 'Unloaded' WHERE PluginId = @PluginId",
            new { PluginId = pluginId, Now = DateTime.UtcNow.ToString("O") });
    }

    public async Task LogPluginEventAsync(string pluginId, string level, string message, string? exception = null)
    {
        await ExecuteAsync(@"
            INSERT INTO __PluginLogs (PluginId, Timestamp, Level, Message, Exception)
            VALUES (@PluginId, @Timestamp, @Level, @Message, @Exception)",
            new { PluginId = pluginId, Timestamp = DateTime.UtcNow.ToString("O"), Level = level, Message = message, Exception = exception });
    }

    public async Task SavePluginStateAsync(string pluginId, string key, string? value)
    {
        await ExecuteAsync(@"
            INSERT OR REPLACE INTO __PluginState (PluginId, Key, Value, UpdatedAt)
            VALUES (@PluginId, @Key, @Value, @UpdatedAt)",
            new { PluginId = pluginId, Key = key, Value = value, UpdatedAt = DateTime.UtcNow.ToString("O") });
    }

    public async Task<string?> GetPluginStateAsync(string pluginId, string key)
    {
        return await QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM __PluginState WHERE PluginId = @PluginId AND Key = @Key",
            new { PluginId = pluginId, Key = key });
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static string Truncate(string s, int max = 200) => s.Length <= max ? s : s[..max] + "...";

    public void Dispose() => _semaphore.Dispose();
}
