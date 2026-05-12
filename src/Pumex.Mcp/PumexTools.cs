using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Pumex.Contracts;
using Pumex.Ipc;

namespace Pumex.Mcp;

[McpServerToolType]
public sealed class PumexTools
{
    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [McpServerTool, Description("Ping the Pumex daemon. Returns 'pong' if the daemon is running, or an error message if it is not reachable.")]
    public static async Task<string> Ping(CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var resp = await client.SendAsync<string>("ping", ct: ct);
            return resp.Success ? "pong" : $"error: {resp.Error}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Search notes using full-text search (FTS5). Supports AND, OR, NOT, phrase queries, and field:value syntax. Returns matching notes with path and snippet.")]
    public static async Task<string> Search(
        [Description("FTS5 full-text query (optional if tag or property filters are provided)")] string? query = null,
        [Description("Filter by tag name — repeat to AND multiple tags")] string[]? tag = null,
        [Description("Filter by frontmatter property in key=value format — repeat to AND multiple")] string[]? property = null,
        [Description("Maximum number of results (default 20)")] int limit = 20,
        [Description("Vault name (optional — omit to search the default vault)")] string? vault = null,
        [Description("Vault root path (optional — absolute path, alternative to vault name)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["limit"] = limit.ToString() };
            if (query is not null) args["query"] = query;
            if (tag is { Length: > 0 }) args["tags"] = string.Join(',', tag);
            if (property is { Length: > 0 }) args["properties"] = string.Join(';', property);
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<List<SearchResult>>("search", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            var results = resp.Data ?? [];
            if (results.Count == 0) return "no matches";

            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.AppendLine($"## {r.Name}");
                sb.AppendLine($"Path: {r.Path}");
                if (!string.IsNullOrEmpty(r.Snippet))
                    sb.AppendLine($"Snippet: {r.Snippet}");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all tags across the vault with their occurrence counts. Returns a JSON array of {tag, count} objects.")]
    public static async Task<string> ListTags(
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string>();
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<List<TagCount>>("tags", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return JsonSerializer.Serialize(resp.Data ?? [], JsonOut);
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find all notes that link to the specified note via wikilinks. Returns a JSON array of file paths.")]
    public static async Task<string> Backlinks(
        [Description("Path or bare note name (filename without .md) to find backlinks for")] string path,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<List<string>>("backlinks", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return JsonSerializer.Serialize(resp.Data ?? [], JsonOut);
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Read a note's full content including frontmatter properties, tags, and body text.")]
    public static async Task<string> ReadNote(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<NoteContent>("note:read", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            var note = resp.Data!;
            var sb = new StringBuilder();
            sb.AppendLine($"Path: {note.Path}");
            if (note.Properties.Count > 0)
            {
                sb.AppendLine("Properties:");
                foreach (var (k, v) in note.Properties)
                    sb.AppendLine($"  {k}: {v}");
            }
            if (note.Tags.Count > 0)
                sb.AppendLine($"Tags: {string.Join(", ", note.Tags.Select(t => "#" + t))}");
            sb.AppendLine();
            sb.Append(note.Body);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all notes in a vault. Returns a JSON array of {path, name, mtime, size} objects.")]
    public static async Task<string> ListNotes(
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string>();
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<List<NoteSummary>>("note:list", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return JsonSerializer.Serialize(resp.Data ?? [], JsonOut);
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all registered vaults. Returns a JSON array of {id, name, path} objects.")]
    public static async Task<string> ListVaults(CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var resp = await client.SendAsync<List<VaultRecord>>("vaults", ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return JsonSerializer.Serialize(resp.Data ?? [], JsonOut);
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("List all YAML frontmatter properties of a note. Returns a JSON array of {key, value} objects.")]
    public static async Task<string> ListProperties(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<List<PropertyEntry>>("property:list", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return JsonSerializer.Serialize(resp.Data ?? [], JsonOut);
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get a single YAML frontmatter property value from a note.")]
    public static async Task<string> GetProperty(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Property key")] string key,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path, ["key"] = key };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<string>("property:get", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return resp.Data ?? "";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Read a daily note. Defaults to today's note when no date is given.")]
    public static async Task<string> ReadDaily(
        [Description("Date in YYYY-MM-DD format (optional, defaults to today)")] string? date = null,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string>();
            if (date is not null) args["date"] = date;
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<NoteContent>("daily:read", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            var note = resp.Data!;
            var sb = new StringBuilder();
            sb.AppendLine($"Path: {note.Path}");
            sb.AppendLine();
            sb.Append(string.IsNullOrWhiteSpace(note.Body) ? "(empty)" : note.Body);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create a new note with the given content. The note is indexed immediately — safe to query right after creation.")]
    public static async Task<string> CreateNote(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Markdown content for the new note")] string content,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path, ["content"] = content };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<NotePathResult>("note:create", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return $"created {resp.Data!.Path}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Append content to an existing note. Adds a new paragraph by default; set inline=true to append on the same line.")]
    public static async Task<string> AppendNote(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Markdown content to append")] string content,
        [Description("Append on the same line instead of a new paragraph (default false)")] bool inline = false,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path, ["content"] = content };
            ApplyVault(args, vault, vault_path);
            if (inline) args["inline"] = "true";

            var resp = await client.SendAsync<NotePathResult>("note:append", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return $"appended to {resp.Data!.Path}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Permanently delete a note from disk. WARNING: deletion is irreversible — the file is removed from disk and cannot be recovered. You MUST pass confirm=true to proceed; omitting it or passing false aborts the operation.")]
    public static async Task<string> DeleteNote(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Must be true to confirm deletion — required safety gate against accidental removal")] bool confirm,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        if (!confirm) return "aborted: pass confirm=true to delete a note";

        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<NotePathResult>("note:delete", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return $"deleted {resp.Data!.Path}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Set a YAML frontmatter property on a note. Creates the property if it does not exist; overwrites it if it does.")]
    public static async Task<string> SetProperty(
        [Description("Path or bare note name (filename without .md)")] string path,
        [Description("Property key")] string key,
        [Description("Property value")] string value,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["path"] = path, ["key"] = key, ["value"] = value };
            ApplyVault(args, vault, vault_path);

            var resp = await client.SendAsync<NotePathResult>("property:set", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return $"set {key}={value} on {resp.Data!.Path}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    [McpServerTool, Description("Append content to a daily note. Defaults to today's note when no date is given; creates the daily note if it does not exist yet.")]
    public static async Task<string> AppendDaily(
        [Description("Markdown content to append")] string content,
        [Description("Date in YYYY-MM-DD format (optional, defaults to today)")] string? date = null,
        [Description("Append on the same line instead of a new paragraph (default false)")] bool inline = false,
        [Description("Vault name (optional)")] string? vault = null,
        [Description("Vault root path (optional)")] string? vault_path = null,
        CancellationToken ct = default)
    {
        var client = new IpcClient();
        try
        {
            var args = new Dictionary<string, string> { ["content"] = content };
            ApplyVault(args, vault, vault_path);
            if (date is not null) args["date"] = date;
            if (inline) args["inline"] = "true";

            var resp = await client.SendAsync<NotePathResult>("daily:append", args, ct: ct);
            if (!resp.Success) return $"error: {resp.Error}";

            return $"appended to {resp.Data!.Path}";
        }
        catch (Exception ex)
        {
            return $"daemon not reachable: {ex.Message}";
        }
    }

    private static void ApplyVault(Dictionary<string, string> args, string? vault, string? vaultPath)
    {
        if (vault is not null) args["vault"] = vault;
        if (vaultPath is not null) args["vaultPath"] = vaultPath;
    }
}
