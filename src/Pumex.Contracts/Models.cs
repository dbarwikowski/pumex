namespace Pumex.Contracts;

public record VaultRecord(long Id, string Name, string Path);

public record SearchResult(string Path, string Name, string Snippet, string? Format = null);

/// <summary>
/// One source in an agent-oriented context pack (<c>pumex context</c>):
/// a relevance-ranked passage plus a verbatim-runnable drill-down pointer.
/// </summary>
/// <param name="RelativePath">Vault-relative path, used as the block heading.</param>
/// <param name="Passage">The best-matching section, multi-line, frontmatter stripped.</param>
/// <param name="Pointer">A <c>pumex read</c> argument: the bare note name when
/// unique in the vault, otherwise the relative path.</param>
/// <param name="Score">Normalised relevance, higher = better (raw FTS5 bm25 is negated).</param>
public record ContextResult(string RelativePath, string Passage, string Pointer, double Score, string Format);

public record TagCount(string Tag, int Count);

public record PropertyEntry(string Key, string Value);

public record NoteSummary(string Path, string Name, long Mtime, long Size, string? Format = null);

// A GFM task-list checkbox extracted from a note. Index is 1-based and stable
// over all checkboxes in document order (independent of any pending filter).
public record CheckboxItem(int Index, bool Checked, string Text);

public record CheckboxToggleResult(string Path, int Index, bool Checked, string Text);

// A task-note (a note under <vault>/tasks/ with type: TASK frontmatter).
public record TaskResult(string Path, string Name);
public record TaskSummary(string Name, string Status, string Created, string Updated, string Completed, string Path);
public record TaskAttachResult(string TaskPath, string AttachmentPath);
