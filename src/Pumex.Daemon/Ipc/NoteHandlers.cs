using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class NoteReadHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;

    public string Command => "note:read";

    public NoteReadHandler(NoteParser parser, IVaultRepository vaults, INoteRepository notes)
    {
        _parser = parser;
        _vaults = vaults;
        _notes = notes;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);
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
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly IInlineIndex _inlineIndex;
    private readonly NoteParser _parser;

    public string Command => "note:create";

    public NoteCreateHandler(IVaultRepository vaults, INoteRepository notes, IInlineIndex inlineIndex, NoteParser parser)
    {
        _vaults = vaults;
        _notes = notes;
        _inlineIndex = inlineIndex;
        _parser = parser;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(
            request.Require("path"), vault, _notes, NoteResolutionMode.Create);
        var content = request.Optional("content") ?? "";

        if (File.Exists(path))
            throw new InvalidOperationException($"Note already exists: {path}. Use note:append to add to it.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, content, ct);
        if (vault is not null) await _inlineIndex.UpsertAsync(vault.Id, path);
        return new NotePathResult(path);
    }
}

public class NoteAppendHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "note:append";

    public NoteAppendHandler(IVaultRepository vaults, INoteRepository notes, IInlineIndex inlineIndex)
    {
        _vaults = vaults;
        _notes = notes;
        _inlineIndex = inlineIndex;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);
        var content = request.Optional("content") ?? "";
        var inline = request.Flag("inline");

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var existing = await File.ReadAllTextAsync(path, ct);

        var prefix = (inline || existing.Length == 0 || existing.EndsWith('\n')) ? "" : "\n";
        var suffix = content.EndsWith('\n') ? "" : "\n";

        await File.AppendAllTextAsync(path, prefix + content + suffix, ct);
        if (vault is not null) await _inlineIndex.UpsertAsync(vault.Id, path);
        return new NotePathResult(path);
    }
}

public class NoteListHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;

    public string Command => "note:list";

    public NoteListHandler(IVaultRepository vaults, INoteRepository notes)
    {
        _vaults = vaults;
        _notes = notes;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        return await _notes.ListNotesAsync(vault?.Id);
    }
}

public class NoteDeleteHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "note:delete";

    public NoteDeleteHandler(IVaultRepository vaults, INoteRepository notes, IInlineIndex inlineIndex)
    {
        _vaults = vaults;
        _notes = notes;
        _inlineIndex = inlineIndex;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        File.Delete(path);
        await _inlineIndex.DeleteAsync(path);
        return new NotePathResult(path);
    }
}
