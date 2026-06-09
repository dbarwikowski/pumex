namespace Pumex.Contracts;

public record VaultRecord(long Id, string Name, string Path);

public record SearchResult(string Path, string Name, string Snippet, string? Format = null);

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
