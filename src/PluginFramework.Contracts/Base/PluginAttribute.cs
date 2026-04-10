namespace PluginFramework.Contracts.Base;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PluginAttribute : Attribute
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "1.0.0";
    public string? Author { get; init; }
}
