namespace PluginFramework.Contracts.Versioning;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class)]
public class InterfaceVersionAttribute : Attribute
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public InterfaceVersionAttribute(int major, int minor, int patch = 0)
        => (Major, Minor, Patch) = (major, minor, patch);

    public Version ToVersion() => new(Major, Minor, Patch);
}
