using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Versioning;

namespace PluginFramework.Contracts.Plugins;

[InterfaceVersion(1, 0, 0)]
public interface IHttpServicePlugin : IPluginBase
{
    /// <summary>Préfixe de route (ex: "/api/mon-plugin")</summary>
    string RoutePrefix { get; }

    /// <summary>Enregistrement des services DI du plugin</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Enregistrement des endpoints Minimal API</summary>
    void ConfigureEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Port personnalisé (null = port par défaut de l'hôte)</summary>
    int? CustomPort => null;
}
