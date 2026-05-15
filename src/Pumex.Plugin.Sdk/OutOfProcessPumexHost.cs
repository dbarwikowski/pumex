using Pumex.Contracts;
using Pumex.Ipc;

namespace Pumex.Plugin.Sdk;

// IPumexHost impl for out-of-proc plugins. Wraps an IpcClient that's pointed
// at the daemon's pipe (PUMEX_DAEMON_PIPE). Each call is a fresh client
// connection — same per-request pattern as the daemon's proxy hop. Plugins
// using this run as a peer of the CLI from the daemon's perspective; the
// daemon's dispatcher fans out per-connection, so reentrant calls (the plugin
// handler calls back into the daemon) work without deadlock.
public sealed class OutOfProcessPumexHost : IPumexHost
{
    private readonly IpcClient _client;

    public OutOfProcessPumexHost(IpcClient client) => _client = client;

    public async Task<IReadOnlyList<VaultRecord>> GetVaultsAsync(CancellationToken ct = default)
    {
        var r = await _client.SendAsync<List<VaultRecord>>("vaults", ct: ct);
        return Unwrap(r);
    }

    public async Task<VaultRecord?> ResolveVaultAsync(string nameOrPath, CancellationToken ct = default)
    {
        // The daemon resolves vault scope through standard IPC args ("vault" or
        // "vaultPath"); we don't currently expose a dedicated resolve command,
        // so do the lookup client-side against the vault list.
        var vaults = await GetVaultsAsync(ct);
        var byName = vaults.FirstOrDefault(v => string.Equals(v.Name, nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;
        var full = Path.GetFullPath(nameOrPath);
        return vaults.FirstOrDefault(v => string.Equals(v.Path, full, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string? query,
        long? vaultId = null,
        int limit = 50,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null,
        CancellationToken ct = default)
    {
        var args = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(query)) args["query"] = query;
        if (vaultId is long id) args["vaultId"] = id.ToString();
        if (limit > 0) args["limit"] = limit.ToString();
        if (tags is { Count: > 0 }) args["tag"] = string.Join(',', tags);
        if (properties is { Count: > 0 })
            args["property"] = string.Join(',', properties.Select(kv => $"{kv.Key}={kv.Value}"));

        var r = await _client.SendAsync<List<SearchResult>>("search", args, ct: ct);
        return Unwrap(r);
    }

    public async Task<NoteContent> ReadNoteAsync(string path, CancellationToken ct = default)
    {
        var r = await _client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = path,
        }, ct: ct);
        return Unwrap(r);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagsAsync(long? vaultId, CancellationToken ct = default)
    {
        var args = new Dictionary<string, string>();
        if (vaultId is long id) args["vaultId"] = id.ToString();
        var r = await _client.SendAsync<List<TagCount>>("tags", args, ct: ct);
        return Unwrap(r);
    }

    private static T Unwrap<T>(IpcResponse<T> response)
    {
        if (!response.Success || response.Data is null)
            throw new InvalidOperationException(response.Error ?? "Daemon returned no data.");
        return response.Data;
    }
}
