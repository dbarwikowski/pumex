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
        var path = IpcRequestExtensions.ResolveNotePath(request.Require("path"), vault);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var doc = _parser.Parse(path);
        var props = doc.Frontmatter
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");

        return new NoteContent(
            Path: path,
            Raw: doc.RawContent,
            Body: doc.Content,
            Properties: props,
            Tags: doc.Tags,
            OutgoingLinks: doc.OutgoingLinks);
    }
}

public class NoteCreateHandler : ICommandHandler
{
    public string Command => "note:create";

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var path = Path.GetFullPath(request.Require("path"));
        var content = request.Optional("content") ?? "";

        if (File.Exists(path))
            throw new InvalidOperationException($"Note already exists: {path}. Use note:append to add to it.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, content);
        // Watcher will pick this up; no need to notify the index synchronously.
        return Task.FromResult<object?>(new NotePathResult(path));
    }
}

public class NoteAppendHandler : ICommandHandler
{
    public string Command => "note:append";

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var path = Path.GetFullPath(request.Require("path"));
        var content = request.Optional("content") ?? "";
        var inline = request.Flag("inline");

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var existing = File.ReadAllText(path);

        var prefix = (inline || existing.Length == 0 || existing.EndsWith('\n')) ? "" : "\n";
        var suffix = content.EndsWith('\n') ? "" : "\n";

        File.AppendAllText(path, prefix + content + suffix);
        return Task.FromResult<object?>(new NotePathResult(path));
    }
}
