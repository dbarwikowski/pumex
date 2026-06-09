using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pumex.Daemon.Ipc;

/// <summary>
/// Splits a note into its YAML frontmatter (as a mutable dictionary) and body,
/// and re-serialises after edits. Shared by <c>property:set</c> and the task
/// status command. Round-tripping through YamlDotNet means cosmetic formatting
/// (key order, quoting style, comments) may not survive — a documented v0.1
/// trade-off.
/// </summary>
internal static class FrontmatterEditor
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static (Dictionary<string, object> Frontmatter, string Body) Split(string raw)
    {
        var normalized = raw.Replace("\r\n", "\n");

        if (!normalized.StartsWith("---"))
            return (new Dictionary<string, object>(), normalized);

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end == -1)
            return (new Dictionary<string, object>(), normalized);

        var yaml = normalized[3..end].Trim();
        var body = normalized[(end + 4)..].TrimStart('\n');

        if (string.IsNullOrWhiteSpace(yaml))
            return (new Dictionary<string, object>(), body);

        // Bubble parse errors up — silently overwriting malformed YAML would lose user data.
        var parsed = Deserializer.Deserialize<Dictionary<string, object>>(yaml)
            ?? new Dictionary<string, object>();
        return (parsed, body);
    }

    public static string Serialize(Dictionary<string, object> frontmatter, string body)
    {
        var yaml = Serializer.Serialize(frontmatter).TrimEnd('\n');
        var bodyPart = string.IsNullOrEmpty(body) ? "" : "\n" + body;
        return $"---\n{yaml}\n---\n{bodyPart}";
    }
}
