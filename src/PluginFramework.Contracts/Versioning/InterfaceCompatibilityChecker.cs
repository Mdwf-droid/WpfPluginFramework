using PluginFramework.Contracts.Base;
using System.Reflection;

namespace PluginFramework.Contracts.Versioning;

public class InterfaceCompatibilityChecker
{
    /// <summary>
    /// Compatibilité : même version majeure, version mineure du plugin >= version mineure requise.
    /// Un plugin compilé contre une ancienne interface reste compatible tant que
    /// la version majeure n'a pas changé et que les méthodes qu'il utilise existent encore.
    /// </summary>
    public CompatibilityResult Check(Type hostInterface, Type pluginType)
    {
        var result = new CompatibilityResult();

        // 1. Vérifier les versions
        var hostVersion = GetVersion(hostInterface);
        var pluginVersion = GetVersion(pluginType);

        if (hostVersion == null)
        {
            result.AddWarning("L'interface hôte n'a pas d'attribut de version");
        }

        if (pluginVersion != null && hostVersion != null)
        {
            result.HostVersion = hostVersion;
            result.PluginVersion = pluginVersion;

            if (pluginVersion.Major != hostVersion.Major)
            {
                result.AddError($"Version majeure incompatible: plugin={pluginVersion}, hôte={hostVersion}");
                return result;
            }

            if (pluginVersion < hostVersion)
            {
                result.AddWarning($"Plugin utilise une ancienne version ({pluginVersion} < {hostVersion}), compatibilité ascendante active");
            }
        }

        // 2. Vérifier que toutes les méthodes de l'interface de base sont implémentées
        var requiredMethods = typeof(IPluginBase).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var implementedMethods = pluginType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        foreach (var required in requiredMethods)
        {
            var found = implementedMethods.Any(m =>
                m.Name == required.Name &&
                m.ReturnType.IsAssignableTo(required.ReturnType) &&
                ParametersMatch(m.GetParameters(), required.GetParameters()));

            if (!found)
            {
                result.AddError($"Méthode manquante: {required.ReturnType.Name} {required.Name}({string.Join(", ", required.GetParameters().Select(p => p.ParameterType.Name))})");
            }
        }

        return result;
    }

    private static bool ParametersMatch(ParameterInfo[] implemented, ParameterInfo[] required)
    {
        if (implemented.Length != required.Length) return false;
        return !implemented.Where((t, i) => t.ParameterType != required[i].ParameterType).Any();
    }

    private static Version? GetVersion(Type type)
    {
        var attr = type.GetCustomAttribute<InterfaceVersionAttribute>();
        return attr?.ToVersion();
    }
}

public class CompatibilityResult
{
    public Version? HostVersion { get; set; }
    public Version? PluginVersion { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsCompatible => Errors.Count == 0;

    public void AddError(string msg) => Errors.Add(msg);
    public void AddWarning(string msg) => Warnings.Add(msg);
}
