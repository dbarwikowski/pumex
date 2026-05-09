namespace Pumex.Daemon;

public record NoteDocument(
    string Path,
    Dictionary<string, object> Frontmatter,
    List<string> Tags,
    List<string> OutgoingLinks,
    string Content,  // bez frontmatter
    string RawContent,
    long Mtime,
    long Size
);
