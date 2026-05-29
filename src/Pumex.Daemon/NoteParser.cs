using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pumex.Daemon;

public partial class NoteParser : IFormatParser
{
    private static readonly string[] HandledExtensions = [".md"];
    public IReadOnlyCollection<string> Extensions => HandledExtensions;

    // Build the YAML deserializer once. DeserializerBuilder.Build() is heavy —
    // benchmarks at 10k notes traced ~70 % of cold-scan allocations to per-call
    // construction. The IDeserializer is documented thread-safe for read-only
    // Deserialize calls.
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

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

    private static Dictionary<string, object> ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new Dictionary<string, object>();

        try
        {
            return YamlDeserializer.Deserialize<Dictionary<string, object>>(yaml)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            // Malformed YAML mustn't crash the daemon.
            return new Dictionary<string, object>();
        }
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
    [GeneratedRegex(@"(?<!\S)#([A-Za-z\u00C0-\u024F][A-Za-z0-9\u00C0-\u024F/_-]*)")]
    private static partial Regex InlineTagRegex();

    [GeneratedRegex(@"\[\[([^\]|#]+)(?:[|#][^\]]*)?\]\]")]
    private static partial Regex WikilinkRegex();
}