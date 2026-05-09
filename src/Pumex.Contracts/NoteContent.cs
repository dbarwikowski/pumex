namespace Pumex.Contracts;

public record NoteContent(
    string Path,
    string Raw,
    string Body,
    Dictionary<string, string> Properties,
    List<string> Tags,
    List<string> OutgoingLinks
);

public record NotePathResult(string Path);
