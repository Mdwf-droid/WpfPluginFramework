using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Plugins;
using PluginFramework.Contracts.Versioning;
using System.IO;

namespace Plugins.Demo.Http;

[Plugin(Id = "demo-http", Name = "Plugin HTTP Démo", Version = "1.0.0")]
[InterfaceVersion(2, 0, 0)]
public class DemoHttpPlugin : IHttpServicePlugin
{
    private IPluginHost _host = null!;

    public string PluginId => "demo-http";
    public string Name => "Plugin HTTP Démo";
    public string Version => "1.0.0";
    public Version InterfaceVersion => new(2, 0, 0);
    public PluginCapabilities Capabilities => PluginCapabilities.HttpService;
    public string RoutePrefix => "/api/demo";

    public Task InitializeAsync(IPluginHost host)
    {
        _host = host;
        _host.Logger.LogInformation("DemoHttpPlugin initialisé (routes sous {Prefix})", RoutePrefix);
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _host.Logger.LogInformation("DemoHttpPlugin arrêté");
        return Task.CompletedTask;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Enregistrer des services propres au plugin si nécessaire
    }

    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/demo/hello", () =>
            Results.Ok(new { message = "Hello from DemoHttpPlugin!", timestamp = DateTime.UtcNow }));

        endpoints.MapGet("/api/demo/health", () =>
            Results.Ok(new { status = "healthy", plugin = "demo-http", version = "1.0.0" }));

        endpoints.MapPost("/api/demo/echo", (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = reader.ReadToEndAsync().Result;
            return Results.Ok(new { echo = body, receivedAt = DateTime.UtcNow });
        });
    }
}
