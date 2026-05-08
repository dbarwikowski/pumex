namespace Pumex.Contracts;

public record VaultRecord(long Id, string Name, string Path);

public record SearchResult(string Path, string Name, string Snippet);

public record TagCount(string Tag, int Count);

public record PropertyEntry(string Key, string Value);
