namespace PluginFramework.Core.Discovery;

public class DiscoveredPluginFile
{
    public required string FullPath { get; init; }
    public required string DirectoryRoot { get; init; }
    public string? RelativePath { get; init; }
    public long FileSize { get; init; }
    public DateTime LastModifiedUtc { get; init; }
}
