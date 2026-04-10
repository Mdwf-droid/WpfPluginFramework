using System.Data;

namespace PluginFramework.Contracts.Services;

public interface IPluginDatabase
{
    Task<int> ExecuteAsync(string sql, object? parameters = null);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null);
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null);
    IDbConnection GetConnection();
}
