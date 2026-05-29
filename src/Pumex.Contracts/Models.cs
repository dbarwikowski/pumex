namespace Pumex.Contracts;

public record VaultRecord(long Id, string Name, string Path);

public record SearchResult(string Path, string Name, string Snippet);

public record TagCount(string Tag, int Count);

public record PropertyEntry(string Key, string Value);

public record NoteSummary(string Path, string Name, long Mtime, long Size, string? Format = null);
