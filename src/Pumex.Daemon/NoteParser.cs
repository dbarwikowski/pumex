using System.Text.RegularExpressions;

namespace Pumex.Daemon;

public partial class NoteParser
{
    public NoteDocument Parse(string filePath)
    {
        var raw = File.ReadAllText(filePath);
        // Replace always allocates a new string; skip when there's no CR to replace.
        var normalized = raw.IndexOf('\r') >= 0 ? raw.Replace("\r\n", "\n") : raw;

        var info = new FileInfo(filePath);
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
        var size = info.Length;

        var (frontmatter, content) = SplitFrontmatter(normalized);
        var tags = ExtractTags(frontmatter, content);
        var links = ExtractWikilinks(content);

        return new NoteDocument(
            Path: filePath,
            Frontmatter: frontmatter,
            Tags: tags,
            OutgoingLinks: links,
            Content: content,
            RawContent: normalized,
            Mtime: mtime,
            Size: size
        );
    }

    private static List<string> ExtractWikilinks(string content)
    {
        // [[Note]] → "Note"; [[Note|Alias]] → "Note"; [[Note#Heading]] → "Note"
        var matches = WikilinkRegex().Matches(content);
        if (matches.Count == 0) return [];

        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value.Trim();
            if (name.Length > 0 && !result.Contains(name)) result.Add(name);
        }
        return result;
    }

    private static (Dictionary<string, object> Frontmatter, string Content) SplitFrontmatter(string raw)
    {
        if (!raw.StartsWith("---"))
            return (new Dictionary<string, object>(), raw);

        var end = raw.IndexOf("\n---", 3);
        if (end == -1)
            return (new Dictionary<string, object>(), raw);

        var yaml = raw[3..end].Trim();
        var content = raw[(end + 4)..].TrimStart('\n');

        return (ParseYaml(yaml), content);
    }

    // Handles key: value, key: [a, b, c], and block list form:
    //   key:
    //     - a
    //     - b
    // Quoted scalars (single or double) are unquoted. Everything else is a string.
    // Complex YAML (anchors, multi-line scalars, nested maps) is silently ignored.
    internal static Dictionary<string, object> ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return [];

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        List<object>? currentList = null;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            // Block list item under currentKey
            if (currentKey is not null && currentList is not null)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    currentList.Add(UnquoteScalar(trimmed[2..].Trim()));
                    continue;
                }
            }

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) { currentKey = null; currentList = null; continue; }

            var key = line[..colonIdx].Trim();
            if (string.IsNullOrEmpty(key) || key.Contains(' '))
            { currentKey = null; currentList = null; continue; }

            var rest = line[(colonIdx + 1)..].Trim();

            if (rest.StartsWith('['))
            {
                var list = ParseInlineList(rest);
                result[key] = list;
                currentKey = key;
                currentList = list;
            }
            else if (rest.Length == 0)
            {
                var list = new List<object>();
                result[key] = list;
                currentKey = key;
                currentList = list;
            }
            else
            {
                result[key] = UnquoteScalar(rest);
                currentKey = key;
                currentList = null;
            }
        }

        return result;
    }

    private static List<object> ParseInlineList(string s)
    {
        var inner = s.TrimStart('[');
        var end = inner.LastIndexOf(']');
        if (end >= 0) inner = inner[..end];
        var result = new List<object>();
        foreach (var item in inner.Split(','))
        {
            var value = UnquoteScalar(item.Trim());
            if (value.Length > 0) result.Add(value);
        }
        return result;
    }

    internal static string UnquoteScalar(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private List<string> ExtractTags(Dictionary<string, object> frontmatter, string content)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Z frontmatter - pole "tags"
        if (frontmatter.TryGetValue("tags", out var tagsValue))
        {
            switch (tagsValue)
            {
                // tags: [tag1, tag2]
                case List<object> list:
                    foreach (var t in list)
                        AddTag(tags, t?.ToString());
                    break;

                // tags: tag1
                case string s:
                    AddTag(tags, s);
                    break;
            }
        }

        // Inline #tagi w treści - pomijaj code blocks i frontmatter
        var inlineTags = InlineTagRegex().Matches(content);
        foreach (Match match in inlineTags)
            AddTag(tags, match.Groups[1].Value);

        return tags.ToList();
    }

    private static void AddTag(HashSet<string> tags, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        // Normalizuj - usuń leading #
        tag = tag.TrimStart('#').Trim();

        if (!string.IsNullOrEmpty(tag))
            tags.Add(tag);
    }

    // #tag, #tag/subtag - nie matchuj URL (#anchor) ani nagłówków (# Heading)
    [GeneratedRegex(@"(?<!\S)#([A-Za-zÀ-ɏ][A-Za-z0-9À-ɏ/_-]*)")]
    private static partial Regex InlineTagRegex();

    [GeneratedRegex(@"\[\[([^\]|#]+)(?:[|#][^\]]*)?\]\]")]
    private static partial Regex WikilinkRegex();
}
