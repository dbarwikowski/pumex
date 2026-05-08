using Pumex.Contracts;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pumex.Daemon.Ipc;

public class PropertyListHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "property:list";

    public PropertyListHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);
        var noteId = await _db.GetNoteIdAsync(path)
            ?? throw new FileNotFoundException($"Note not in index: {path}");
        return await _db.GetPropertiesAsync(noteId);
    }
}

public class PropertyGetHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "property:get";

    public PropertyGetHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);
        var key = request.Require("key");

        var noteId = await _db.GetNoteIdAsync(path)
            ?? throw new FileNotFoundException($"Note not in index: {path}");
        return await _db.GetPropertyAsync(noteId, key)
            ?? throw new KeyNotFoundException($"Property '{key}' not set on note");
    }
}

/// <summary>
/// Rewrites the note's YAML frontmatter to set/update <c>key</c>. Splices the
/// block via YamlDotNet round-trip, so cosmetic formatting (key order, quoting
/// style, comments) may not survive — documented v0.1 trade-off.
/// </summary>
public class PropertySetHandler : ICommandHandler
{
    private readonly IndexDb _db;
    private readonly NoteParser _parser;

    public string Command => "property:set";

    public PropertySetHandler(IndexDb db, NoteParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);
        var key = request.Require("key");
        var value = request.Optional("value") ?? "";

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var raw = await File.ReadAllTextAsync(path, ct);
        var normalized = raw.Replace("\r\n", "\n");

        var (frontmatter, body) = SplitFrontmatter(normalized);
        frontmatter[key] = value;

        var newContent = SerializeWithFrontmatter(frontmatter, body);
        await File.WriteAllTextAsync(path, newContent, ct);
        if (vault is not null) await InlineIndex.UpsertAsync(_db, _parser, vault.Id, path);
        return new NotePathResult(path);
    }

    private static (Dictionary<string, object> Frontmatter, string Body) SplitFrontmatter(string raw)
    {
        if (!raw.StartsWith("---"))
            return (new Dictionary<string, object>(), raw);

        var end = raw.IndexOf("\n---", 3);
        if (end == -1)
            return (new Dictionary<string, object>(), raw);

        var yaml = raw[3..end].Trim();
        var body = raw[(end + 4)..].TrimStart('\n');

        if (string.IsNullOrWhiteSpace(yaml))
            return (new Dictionary<string, object>(), body);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Bubble parse errors up — silently overwriting malformed YAML would lose user data.
        var parsed = deserializer.Deserialize<Dictionary<string, object>>(yaml)
            ?? new Dictionary<string, object>();
        return (parsed, body);
    }

    private static string SerializeWithFrontmatter(Dictionary<string, object> frontmatter, string body)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(frontmatter).TrimEnd('\n');
        var bodyPart = string.IsNullOrEmpty(body) ? "" : "\n" + body;
        return $"---\n{yaml}\n---\n{bodyPart}";
    }
}
