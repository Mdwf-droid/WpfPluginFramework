using Microsoft.Extensions.Logging;
using NSubstitute;
using PluginFramework.Configuration;
using PluginFramework.Core.Discovery;
using System.IO;
using Xunit;

namespace PluginFramework.Tests;

public class DiscoveryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogger _logger;

    public DiscoveryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PluginTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logger = Substitute.For<ILogger>();
    }

    [Fact]
    public void DiscoverAll_FindsPluginsInConfiguredDirectories()
    {
        // Créer des faux fichiers plugin
        var pluginDir = Path.Combine(_tempDir, "Plugins");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllBytes(Path.Combine(pluginDir, "Test.Plugin.dll"), new byte[] { 0x4D, 0x5A }); // MZ header

        var settings = new PluginSettings
        {
            Directories = new() { new() { Path = pluginDir, Recursive = true } },
            SearchPattern = "*.Plugin.dll"
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Single(results);
        Assert.Contains("Test.Plugin.dll", results[0].FullPath);
    }

    [Fact]
    public void DiscoverAll_SkipsEmptyFiles()
    {
        var pluginDir = Path.Combine(_tempDir, "Plugins2");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllBytes(Path.Combine(pluginDir, "Empty.Plugin.dll"), Array.Empty<byte>());

        var settings = new PluginSettings
        {
            Directories = new() { new() { Path = pluginDir, Recursive = true } },
            SearchPattern = "*.Plugin.dll"
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Empty(results);
    }

    [Fact]
    public void DiscoverAll_IgnoresConfiguredDirectories()
    {
        var root = Path.Combine(_tempDir, "Plugins3");
        var binDir = Path.Combine(root, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllBytes(Path.Combine(binDir, "Hidden.Plugin.dll"), new byte[] { 0x4D, 0x5A });

        var settings = new PluginSettings
        {
            Directories = new() { new() { Path = root, Recursive = true } },
            SearchPattern = "*.Plugin.dll",
            IgnoreDirectories = new() { "bin" }
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Empty(results);
    }

    [Fact]
    public void DiscoverAll_RespectsMaxRecursionDepth()
    {
        // Créer une arborescence profonde
        var current = Path.Combine(_tempDir, "Deep");
        for (int i = 0; i < 15; i++)
        {
            current = Path.Combine(current, $"level{i}");
            Directory.CreateDirectory(current);
        }
        File.WriteAllBytes(Path.Combine(current, "Deep.Plugin.dll"), new byte[] { 0x4D, 0x5A });

        var settings = new PluginSettings
        {
            Directories = new() { new() { Path = Path.Combine(_tempDir, "Deep"), Recursive = true } },
            SearchPattern = "*.Plugin.dll",
            MaxRecursionDepth = 5
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Empty(results); // Trop profond
    }

    [Fact]
    public void DiscoverAll_OptionalMissingDirectory_DoesNotThrow()
    {
        var settings = new PluginSettings
        {
            Directories = new() { new() { Path = "/nonexistent/path", Optional = true } },
            SearchPattern = "*.Plugin.dll"
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Empty(results);
    }

    [Fact]
    public void DiscoverAll_DeduplicatesSameFile()
    {
        var pluginDir = Path.Combine(_tempDir, "Dedup");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllBytes(Path.Combine(pluginDir, "Same.Plugin.dll"), new byte[] { 0x4D, 0x5A });

        var settings = new PluginSettings
        {
            Directories = new()
            {
                new() { Path = pluginDir, Recursive = true },
                new() { Path = pluginDir, Recursive = true }  // Même dossier 2 fois
            },
            SearchPattern = "*.Plugin.dll"
        };

        var service = new PluginDiscoveryService(settings, _logger);
        var results = service.DiscoverAll();

        Assert.Single(results);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* cleanup best effort */ }
    }
}
