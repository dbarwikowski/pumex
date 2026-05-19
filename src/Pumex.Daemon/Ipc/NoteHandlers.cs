using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class NoteReadHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IndexDb _db;

    public string Command => "note:read";

    public NoteReadHandler(NoteParser parser, IndexDb db)
    {
        _parser = parser;
        _db = db;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var doc = _parser.Parse(path);
        var props = doc.Frontmatter
            .Where(kv => !kv.Key.Equals("tags", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => FormatFrontmatterValue(kv.Value));

        return new NoteContent(
            Path: path,
            Raw: doc.RawContent,
            Body: doc.Content,
            Properties: props,
            Tags: doc.Tags,
            OutgoingLinks: doc.OutgoingLinks);
    }

    private static string FormatFrontmatterValue(object? value) => value switch
    {
        null => "",
        string s => s,
        System.Collections.IDictionary dict => string.Join(", ",
            dict.Keys.Cast<object>().Select(k => $"{k}: {FormatFrontmatterValue(dict[k])}")),
        System.Collections.IEnumerable seq => string.Join(", ",
            seq.Cast<object?>().Select(e => e?.ToString() ?? "")),
        _ => value.ToString() ?? "",
    };
}

public class NoteCreateHandler : ICommandHandler
{
    private readonly IndexDb _db;
    private readonly NoteParser _parser;

    public string Command => "note:create";

    public NoteCreateHandler(IndexDb db, NoteParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(
            request.Require("path"), vault, _db, NoteResolutionMode.Create);
        var content = request.Optional("content") ?? "";

        if (File.Exists(path))
            throw new InvalidOperationException($"Note already exists: {path}. Use note:append to add to it.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, content, ct);
        if (vault is not null) await InlineIndex.UpsertAsync(_db, _parser, vault.Id, path);
        return new NotePathResult(path);
    }
}

public class NoteAppendHandler : ICommandHandler
{
    private readonly IndexDb _db;
    private readonly NoteParser _parser;

    public string Command => "note:append";

    public NoteAppendHandler(IndexDb db, NoteParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);
        var content = request.Optional("content") ?? "";
        var inline = request.Flag("inline");

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var existing = await File.ReadAllTextAsync(path, ct);

        var prefix = (inline || existing.Length == 0 || existing.EndsWith('\n')) ? "" : "\n";
        var suffix = content.EndsWith('\n') ? "" : "\n";

        await File.AppendAllTextAsync(path, prefix + content + suffix, ct);
        if (vault is not null) await InlineIndex.UpsertAsync(_db, _parser, vault.Id, path);
        return new NotePathResult(path);
    }
}

public class NoteListHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "note:list";

    public NoteListHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        return await _db.ListNotesAsync(vault?.Id);
    }
}

public class NoteDeleteHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "note:delete";

    public NoteDeleteHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _db);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        File.Delete(path);
        await InlineIndex.DeleteAsync(_db, path);
        return new NotePathResult(path);
    }
}
