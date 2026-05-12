using System.Text.Json;
using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

/// <summary>
/// Resolution of daily-note paths against a vault: pull the configured folder
/// + date format from <c>.pumex/config.json</c>, fall back to <c>daily/</c> +
/// <c>yyyy-MM-dd</c>. Override the date with the request arg <c>date</c>.
/// </summary>
internal static class Daily
{
    public static async Task<VaultConfig> LoadConfigAsync(VaultRecord vault, CancellationToken ct)
    {
        var configPath = Path.Combine(vault.Path, PumexPaths.VaultMarkerDir, PumexPaths.VaultConfigFile);
        if (!File.Exists(configPath))
            return new VaultConfig(vault.Name, DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            return JsonSerializer.Deserialize(json, PumexJsonContext.Default.VaultConfig)
                ?? new VaultConfig(vault.Name, DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);
        }
        catch
        {
            // Bad config shouldn't break daily commands; fall back to defaults.
            return new VaultConfig(vault.Name, DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);
        }
    }

    public static string PathFor(VaultRecord vault, VaultConfig config, DateTime? date = null)
    {
        var d = date ?? DateTime.Now;
        var fileName = d.ToString(config.EffectiveDailyFormat) + ".md";
        return Path.Combine(vault.Path, config.EffectiveDailyFolder, fileName);
    }

    public static DateTime? ParseDate(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? null
            : DateTime.TryParseExact(raw, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
                ? d
                : DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.None, out var loose)
                    ? loose
                    : throw new ArgumentException($"Cannot parse date '{raw}' (try yyyy-MM-dd).");
}

public class DailyReadHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IndexDb _db;

    public string Command => "daily:read";

    public DailyReadHandler(NoteParser parser, IndexDb db)
    {
        _parser = parser;
        _db = db;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db)
            ?? throw new ArgumentException("daily commands need a vault. Pass --vault NAME, --vault-path PATH, or run from inside a vault.");
        var config = await Daily.LoadConfigAsync(vault, ct);
        var path = Daily.PathFor(vault, config, Daily.ParseDate(request.Optional("date")));

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "", ct);
            await InlineIndex.UpsertAsync(_db, _parser, vault.Id, path);
        }

        var doc = _parser.Parse(path);
        var props = doc.Frontmatter.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
        return new NoteContent(path, doc.RawContent, doc.Content, props, doc.Tags, doc.OutgoingLinks);
    }
}

public class DailyAppendHandler : ICommandHandler
{
    private readonly NoteParser _parser;
    private readonly IndexDb _db;

    public string Command => "daily:append";

    public DailyAppendHandler(NoteParser parser, IndexDb db)
    {
        _parser = parser;
        _db = db;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db)
            ?? throw new ArgumentException("daily commands need a vault. Pass --vault NAME, --vault-path PATH, or run from inside a vault.");
        var config = await Daily.LoadConfigAsync(vault, ct);
        var path = Daily.PathFor(vault, config, Daily.ParseDate(request.Optional("date")));
        var content = request.Optional("content") ?? "";
        var inline = request.Flag("inline");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var suffix = content.EndsWith('\n') ? "" : "\n";
            await File.WriteAllTextAsync(path, content + suffix, ct);
        }
        else
        {
            var existing = await File.ReadAllTextAsync(path, ct);
            var prefix = (inline || existing.Length == 0 || existing.EndsWith('\n')) ? "" : "\n";
            var suffix = content.EndsWith('\n') ? "" : "\n";
            await File.AppendAllTextAsync(path, prefix + content + suffix, ct);
        }

        await InlineIndex.UpsertAsync(_db, _parser, vault.Id, path);
        return new NotePathResult(path);
    }
}
