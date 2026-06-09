using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class PropertyListHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;

    public string Command => "property:list";

    public PropertyListHandler(IVaultRepository vaults, INoteRepository notes)
    {
        _vaults = vaults;
        _notes = notes;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        // Read-only: non-Markdown files (e.g. JSON) can expose properties, so resolve
        // them by explicit extension like `read`/`backlinks` do. Writing (property:set)
        // stays Markdown-only.
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes, allowNonMarkdown: true);
        var noteId = await _notes.GetNoteIdAsync(path)
            ?? throw new FileNotFoundException($"Note not in index: {path}");
        return await _notes.GetPropertiesAsync(noteId);
    }
}

public class PropertyGetHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;

    public string Command => "property:get";

    public PropertyGetHandler(IVaultRepository vaults, INoteRepository notes)
    {
        _vaults = vaults;
        _notes = notes;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        // Read-only: resolve non-Markdown targets too (see PropertyListHandler).
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes, allowNonMarkdown: true);
        var key = request.Require("key");

        var noteId = await _notes.GetNoteIdAsync(path)
            ?? throw new FileNotFoundException($"Note not in index: {path}");
        return await _notes.GetPropertyAsync(noteId, key)
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
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly IInlineIndex _inlineIndex;
    private readonly NoteParser _parser;

    public string Command => "property:set";

    public PropertySetHandler(IVaultRepository vaults, INoteRepository notes, IInlineIndex inlineIndex, NoteParser parser)
    {
        _vaults = vaults;
        _notes = notes;
        _inlineIndex = inlineIndex;
        _parser = parser;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);
        var key = request.Require("key");
        var value = request.Optional("value") ?? "";

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var raw = await File.ReadAllTextAsync(path, ct);
        var (frontmatter, body) = FrontmatterEditor.Split(raw);
        frontmatter[key] = value;

        var newContent = FrontmatterEditor.Serialize(frontmatter, body);
        await File.WriteAllTextAsync(path, newContent, ct);
        if (vault is not null) await _inlineIndex.UpsertAsync(vault.Id, path);
        return new NotePathResult(path);
    }
}
