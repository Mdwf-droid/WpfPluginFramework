using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace PluginFramework.Core.Loading;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _shadowCopyPath;

    private static readonly HashSet<string> SharedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PluginFramework.Contracts",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Configuration",
        "Quartz",
        // WPF / .NET runtime
        "PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml",
        "UIAutomationTypes", "UIAutomationProvider", "ReachFramework",
        "System.Private.CoreLib", "netstandard", "mscorlib",
        "System.Runtime", "System.Collections", "System.Linq",
        "System.Threading", "System.IO", "System.Text",
        "System.Reflection", "System.ComponentModel",
        "System.ObjectModel", "System.Diagnostics",
        "System.Security", "System.Xml", "System.Globalization",
        "System.Resources", "System.Drawing"
    };

    public string ShadowCopyPath => _shadowCopyPath;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        var pluginDir = Path.GetDirectoryName(pluginPath)!;
        var pluginName = Path.GetFileNameWithoutExtension(pluginPath);

        // Shadow copy → dossier temporaire unique
        _shadowCopyPath = Path.Combine(
            Path.GetTempPath(), "PluginFramework", "ShadowCopy",
            pluginName, Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(_shadowCopyPath);

        // Copier toutes les DLLs du dossier du plugin
        foreach (var file in Directory.GetFiles(pluginDir, "*.dll"))
        {
            var destFile = Path.Combine(_shadowCopyPath, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        // Copier aussi les fichiers de config (.json, .xml)
        foreach (var file in Directory.GetFiles(pluginDir, "*.json")
                     .Concat(Directory.GetFiles(pluginDir, "*.xml")))
        {
            File.Copy(file, Path.Combine(_shadowCopyPath, Path.GetFileName(file)), overwrite: true);
        }

        // Le resolver pointe sur le shadow copy
        var shadowPluginPath = Path.Combine(_shadowCopyPath, Path.GetFileName(pluginPath));
        _resolver = new AssemblyDependencyResolver(shadowPluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Les assemblies partagées sont résolues par le contexte parent (hôte)
        if (IsShared(assemblyName.Name))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }

    private static bool IsShared(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return SharedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public void CleanupShadowCopy()
    {
        try
        {
            if (Directory.Exists(_shadowCopyPath))
                Directory.Delete(_shadowCopyPath, recursive: true);
        }
        catch { /* best effort */ }
    }
}
