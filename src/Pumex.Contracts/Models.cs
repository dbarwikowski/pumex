namespace Pumex.Contracts;

public record VaultRecord(long Id, string Name, string Path);

public record SearchResult(string Path, string Name, string Snippet);

public record TagCount(string Tag, int Count);

public record PropertyEntry(string Key, string Value);

public record NoteSummary(string Path, string Name, long Mtime, long Size);

// Wire shape for plugin:list responses. `Kind` is "in-process" or
// "out-of-process"; `Pipe` is non-null only for out-of-process plugins;
// `Version` is null for in-process plugins where the registry doesn't carry it
// (in-proc plugins live behind PluginRegistry without manifest context).
public record PluginInfo(
    string Name,
    string? Version,
    string Kind,
    string? Pipe,
    IReadOnlyList<string> Commands);
