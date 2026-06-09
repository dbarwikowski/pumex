using System.Text.RegularExpressions;
using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

/// <summary>
/// Task-note conventions: a task is a folder <c>tasks/task_yyyy-MM-dd_NN/</c>
/// holding a Markdown note plus any attachments. The note carries
/// <c>status</c>/<c>type: TASK</c>/timestamp frontmatter. Task notes are ordinary
/// indexed Markdown notes; this layer is convenience + the folder scaffold.
/// </summary>
internal static partial class Tasks
{
    public const string Folder = "tasks";

    public static string Root(VaultRecord vault) => Path.Combine(vault.Path, Folder);

    /// <summary>Sanitises a task name: trims, collapses whitespace to '_', and
    /// rejects anything outside <c>[A-Za-z0-9_-]</c>.</summary>
    public static string SanitizeName(string raw)
    {
        var collapsed = WhitespaceRegex().Replace(raw.Trim(), "_");
        if (collapsed.Length == 0 || !TokenRegex().IsMatch(collapsed))
            throw new ArgumentException(
                $"Invalid task name '{raw}'. Allowed characters: letters, digits, '_' and '-' (spaces become '_').");
        return collapsed;
    }

    public static string ValidateStatus(string raw)
    {
        if (!TokenRegex().IsMatch(raw))
            throw new ArgumentException(
                $"Invalid status '{raw}'. Allowed characters: letters, digits, '_' and '-' (no spaces).");
        return raw;
    }

    /// <summary>Folder for a new task on <paramref name="date"/>: the next free
    /// <c>task_yyyy-MM-dd_NN</c> counter (≥2 digits, grows past 99).</summary>
    public static string NextTaskDir(string vaultPath, DateTime date)
    {
        var root = Path.Combine(vaultPath, Folder);
        Directory.CreateDirectory(root);
        var prefix = $"task_{date:yyyy-MM-dd}_";

        var max = -1;
        foreach (var dir in Directory.EnumerateDirectories(root, prefix + "*"))
        {
            var suffix = Path.GetFileName(dir)[prefix.Length..];
            if (int.TryParse(suffix, out var n) && n > max) max = n;
        }
        return Path.Combine(root, prefix + (max + 1).ToString("D2"));
    }

    /// <summary>Atomically allocates the next free task folder for <paramref name="date"/>
    /// and writes the scaffolded note. The <c>NextTaskDir</c>-scan/write pair is racy under
    /// concurrent creates, so this claims the note file with <c>FileMode.CreateNew</c> and
    /// retries the next counter slot if another request won it first.</summary>
    public static async Task<string> CreateTaskNoteAsync(
        string vaultPath, DateTime date, string name, string content, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var dir = NextTaskDir(vaultPath, date);
            Directory.CreateDirectory(dir);
            var candidate = Path.Combine(dir, name + ".md");
            try
            {
                await using var fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(fs);
                await writer.WriteAsync(content.AsMemory(), ct);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another create claimed this slot — retry with the next counter.
            }
        }
        throw new IOException("Could not allocate a unique task folder after multiple attempts.");
    }

    public static string Scaffold(string name, DateTime date, string? content)
    {
        var d = date.ToString("yyyy-MM-dd");
        var fm = $"---\ncreated: {d}\nupdated: {d}\ncompleted:\nstatus: NEW\ntype: TASK\nname: {name}\n---\n";
        if (string.IsNullOrEmpty(content)) return fm;
        var body = content.EndsWith('\n') ? content : content + "\n";
        return fm + "\n" + body;
    }

    /// <summary>Resolves a task reference to its note path. A path (absolute or
    /// containing separators) is used directly; a bare name is matched against
    /// <c>tasks/**/&lt;name&gt;.md</c>. Duplicate names error — pass a path.</summary>
    public static string Resolve(VaultRecord vault, string nameOrPath)
    {
        if (Path.IsPathFullyQualified(nameOrPath) || nameOrPath.Contains('/') || nameOrPath.Contains('\\'))
        {
            var candidate = Path.IsPathFullyQualified(nameOrPath)
                ? Path.GetFullPath(nameOrPath)
                : Path.GetFullPath(Path.Combine(vault.Path, nameOrPath));

            // A path must stay under <vault>/tasks — otherwise task:read/status/attach
            // could be steered at arbitrary files via '..' or an absolute path.
            var taskRoot = Path.GetFullPath(Root(vault));
            var rel = Path.GetRelativePath(taskRoot, candidate);
            if (rel == ".." || rel.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(rel))
                throw new UnauthorizedAccessException(
                    $"Task path '{nameOrPath}' must remain under <vault>/{Folder}.");
            return candidate;
        }

        var name = SanitizeName(nameOrPath);
        var root = Root(vault);
        var fileName = name + ".md";

        var matches = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"No task named '{name}' in vault '{vault.Name}'."),
            _ => throw new InvalidOperationException(
                $"Ambiguous task name '{name}': {matches.Count} matches in vault '{vault.Name}'. Use a path instead."),
        };
    }

    public static bool IsTask(IReadOnlyDictionary<string, object> frontmatter) =>
        frontmatter.TryGetValue("type", out var v) &&
        string.Equals(v?.ToString(), "TASK", StringComparison.OrdinalIgnoreCase);

    public static TaskSummary SummaryFrom(IReadOnlyDictionary<string, object> fm, string path)
    {
        string Get(string k) => fm.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
        var name = Get("name");
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
        return new TaskSummary(name, Get("status"), Get("created"), Get("updated"), Get("completed"), path);
    }

    private static VaultRecord RequireVault(VaultRecord? vault) =>
        vault ?? throw new ArgumentException(
            "task commands need a vault. Pass --vault NAME, --vault-path PATH, or run from inside a vault.");

    public static async Task<VaultRecord> ResolveVault(IpcRequest request, IVaultRepository vaults) =>
        RequireVault(await request.ResolveVaultAsync(vaults));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenRegex();
}

public class TaskCreateHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "task:create";

    public TaskCreateHandler(IVaultRepository vaults, IInlineIndex inlineIndex)
    {
        _vaults = vaults;
        _inlineIndex = inlineIndex;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await Tasks.ResolveVault(request, _vaults);
        var name = Tasks.SanitizeName(request.Require("name"));
        var date = Daily.ParseDate(request.Optional("date")) ?? DateTime.Now;

        var content = Tasks.Scaffold(name, date, request.Optional("content"));
        var path = await Tasks.CreateTaskNoteAsync(vault.Path, date, name, content, ct);
        await _inlineIndex.UpsertAsync(vault.Id, path);
        return new TaskResult(path, name);
    }
}

public class TaskReadHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IVaultRepository _vaults;

    public string Command => "task:read";

    public TaskReadHandler(NoteParser parser, IVaultRepository vaults)
    {
        _parser = parser;
        _vaults = vaults;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await Tasks.ResolveVault(request, _vaults);
        var path = Tasks.Resolve(vault, request.Require("name"));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Task note not found: {path}");

        var doc = _parser.Parse(path);
        var props = doc.Frontmatter
            .Where(kv => !kv.Key.Equals("tags", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
        return new NoteContent(path, doc.RawContent, doc.Content, props, doc.Tags, doc.OutgoingLinks);
    }
}

public class TaskListHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IVaultRepository _vaults;

    public string Command => "task:list";

    public TaskListHandler(NoteParser parser, IVaultRepository vaults)
    {
        _parser = parser;
        _vaults = vaults;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await Tasks.ResolveVault(request, _vaults);
        var root = Tasks.Root(vault);

        var summaries = new List<TaskSummary>();
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                var doc = _parser.Parse(file);
                if (Tasks.IsTask(doc.Frontmatter))
                    summaries.Add(Tasks.SummaryFrom(doc.Frontmatter, file));
            }
        }

        var statusFilter = request.Optional("status") is { Length: > 0 } s
            ? s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
        var openOnly = request.Flag("open");

        IEnumerable<TaskSummary> filtered = summaries;
        if (statusFilter is { Length: > 0 })
            filtered = filtered.Where(t => statusFilter.Contains(t.Status, StringComparer.OrdinalIgnoreCase));
        if (openOnly)
            filtered = filtered.Where(t => !t.Status.Equals("DONE", StringComparison.OrdinalIgnoreCase));

        return filtered
            .OrderByDescending(t => t.Created, StringComparer.Ordinal)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public class TaskStatusHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "task:status";

    public TaskStatusHandler(IVaultRepository vaults, IInlineIndex inlineIndex)
    {
        _vaults = vaults;
        _inlineIndex = inlineIndex;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await Tasks.ResolveVault(request, _vaults);
        var path = Tasks.Resolve(vault, request.Require("name"));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Task note not found: {path}");

        var raw = await File.ReadAllTextAsync(path, ct);
        var (fm, body) = FrontmatterEditor.Split(raw);

        // No value → get.
        if (request.Optional("value") is not { } value)
            return fm.TryGetValue("status", out var current) ? current?.ToString() ?? "" : "";

        // Value → set, with timestamp side-effects.
        var status = Tasks.ValidateStatus(value);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        fm["status"] = status;
        fm["updated"] = today;
        fm["completed"] = status.Equals("DONE", StringComparison.OrdinalIgnoreCase) ? today : "";

        await File.WriteAllTextAsync(path, FrontmatterEditor.Serialize(fm, body), ct);
        await _inlineIndex.UpsertAsync(vault.Id, path);
        return Tasks.SummaryFrom(fm, path);
    }
}

public class TaskAttachHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "task:attach";

    public TaskAttachHandler(IVaultRepository vaults, IInlineIndex inlineIndex)
    {
        _vaults = vaults;
        _inlineIndex = inlineIndex;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await Tasks.ResolveVault(request, _vaults);
        var path = Tasks.Resolve(vault, request.Require("name"));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Task note not found: {path}");

        var source = request.Require("file");
        if (!File.Exists(source))
            throw new FileNotFoundException($"Attachment source not found: {source}");

        var fileName = Path.GetFileName(source);
        var dest = Path.Combine(Path.GetDirectoryName(path)!, fileName);
        if (File.Exists(dest))
            throw new InvalidOperationException($"Attachment already exists: {dest}");

        File.Move(source, dest);

        var existing = await File.ReadAllTextAsync(path, ct);
        var prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";
        await File.AppendAllTextAsync(path, $"{prefix}[[{fileName}]]\n", ct);
        await _inlineIndex.UpsertAsync(vault.Id, path);

        return new TaskAttachResult(path, dest);
    }
}

public class NoteTasksHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;

    public string Command => "note:tasks";

    public NoteTasksHandler(IVaultRepository vaults, INoteRepository notes)
    {
        _vaults = vaults;
        _notes = notes;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var raw = await File.ReadAllTextAsync(path, ct);
        return CheckboxScanner.Items(raw);
    }
}

public class NoteCheckHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly IInlineIndex _inlineIndex;

    public string Command => "note:check";

    public NoteCheckHandler(IVaultRepository vaults, INoteRepository notes, IInlineIndex inlineIndex)
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
        if (!int.TryParse(request.Require("index"), out var index))
            throw new ArgumentException("index must be a number");

        var raw = await File.ReadAllTextAsync(path, ct);
        var (content, item) = CheckboxScanner.Toggle(raw, index);
        await File.WriteAllTextAsync(path, content, ct);
        if (vault is not null) await _inlineIndex.UpsertAsync(vault.Id, path);

        return new CheckboxToggleResult(path, item.Index, item.Checked, item.Text);
    }
}
