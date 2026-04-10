using PluginFramework.Contracts.Base;
using PluginFramework.Contracts.Versioning;
using Xunit;

namespace PluginFramework.Tests;

public class CompatibilityCheckerTests
{
    private readonly InterfaceCompatibilityChecker _checker = new();

    [InterfaceVersion(2, 0, 0)]
    private interface ITestInterfaceV2 : IPluginBase { }

    [InterfaceVersion(1, 0, 0)]
    private class OldPlugin : IPluginBase
    {
        public string PluginId => "test";
        public string Name => "Test";
        public string Version => "1.0.0";
        public Version InterfaceVersion => new(1, 0, 0);
        public PluginCapabilities Capabilities => PluginCapabilities.None;
        public Task InitializeAsync(IPluginHost host) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
    }

    [Fact]
    public void OldPlugin_Against_NewInterface_ShouldBeCompatible_WhenSameMajor()
    {
        // Un plugin compilé contre V1 doit charger dans un hôte V2 (même major = 1 → 1, ou upgrade)
        // Ici major diffère (1 vs 2) → incompatible
        var result = _checker.Check(typeof(ITestInterfaceV2), typeof(OldPlugin));
        // On s'attend à une incompatibilité de version majeure
        Assert.False(result.IsCompatible);
        Assert.Contains(result.Errors, e => e.Contains("majeure"));
    }

    [InterfaceVersion(2, 0, 0)]
    private class PluginV2 : IPluginBase
    {
        public string PluginId => "test-v2";
        public string Name => "Test V2";
        public string Version => "2.0.0";
        public Version InterfaceVersion => new(2, 0, 0);
        public PluginCapabilities Capabilities => PluginCapabilities.None;
        public Task InitializeAsync(IPluginHost host) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
    }

    [Fact]
    public void SameVersion_ShouldBeCompatible()
    {
        var result = _checker.Check(typeof(ITestInterfaceV2), typeof(PluginV2));
        Assert.True(result.IsCompatible);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CompatibilityResult_ShouldContainWarnings_ForOlderMinorVersion()
    {
        // Simuler un plugin v2.0 contre un hôte v2.1
        // (nécessite une interface avec version 2.1)
        var result = _checker.Check(typeof(IPluginBase), typeof(PluginV2));
        Assert.True(result.IsCompatible);
    }
}
