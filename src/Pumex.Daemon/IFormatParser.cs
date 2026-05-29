namespace Pumex.Daemon;

/// <summary>
/// Parses a file of a particular text format into a <see cref="NoteDocument"/>.
/// Markdown is the reference implementation (<see cref="NoteParser"/>); future
/// formats (CSV, JSON, YAML, …) plug in by implementing this interface and
/// declaring the extensions they handle. A parser may populate frontmatter
/// (top-level scalar properties), tags, and outgoing links; non-Markdown
/// parsers typically leave tags and links empty.
/// </summary>
public interface IFormatParser
{
    /// <summary>
    /// Extensions this parser handles, lowercase and dot-prefixed (e.g. <c>.md</c>).
    /// </summary>
    IReadOnlyCollection<string> Extensions { get; }

    NoteDocument Parse(string filePath);
}
