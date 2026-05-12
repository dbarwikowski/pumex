using System.Text;
using Pumex.Contracts;

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
/// Rewrites the note's YAML frontmatter to set/update <c>key</c>. Uses a custom
/// round-trip (parse → mutate → serialize) so complex YAML (anchors, multi-line
/// scalars) is silently flattened — documented v0.1 trade-off.
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

        return (NoteParser.ParseYaml(yaml), body);
    }

    private static string SerializeWithFrontmatter(Dictionary<string, object> frontmatter, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        foreach (var (k, v) in frontmatter)
        {
            switch (v)
            {
                case List<object> list:
                    sb.AppendLine($"{k}:");
                    foreach (var item in list)
                        sb.AppendLine($"  - {QuoteScalar(item?.ToString() ?? "")}");
                    break;
                default:
                    sb.AppendLine($"{k}: {QuoteScalar(v?.ToString() ?? "")}");
                    break;
            }
        }
        sb.AppendLine("---");
        if (!string.IsNullOrEmpty(body))
            sb.Append(body);
        return sb.ToString();
    }

    // Quote a scalar value if it could be misinterpreted by a YAML parser.
    private static string QuoteScalar(string value)
    {
        if (value.Length == 0) return "\"\"";
        if (value.Contains(':') || value.Contains('#') || value.Contains('\\') ||
            value.Contains('"') || value[0] == ' ' || value[^1] == ' ' ||
            value is "true" or "false" or "null" or "yes" or "no" or "on" or "off")
        {
            return '"' + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
        }
        return value;
    }
}
