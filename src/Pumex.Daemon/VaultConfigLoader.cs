using System.Text.Json;
using Pumex.Contracts;

namespace Pumex.Daemon;

/// <summary>
/// Reads a vault's <c>.pumex/config.json</c>. A missing or malformed file falls
/// back to defaults (Markdown only) so a bad config never stops indexing.
/// </summary>
public static class VaultConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static VaultConfig Load(string vaultRoot, string vaultName)
    {
        var configPath = PumexPaths.VaultConfigPath(vaultRoot);
        if (!File.Exists(configPath))
            return Fallback(vaultName);

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<VaultConfig>(json, JsonOptions) ?? Fallback(vaultName);
        }
        catch
        {
            return Fallback(vaultName);
        }
    }

    private static VaultConfig Fallback(string vaultName) =>
        new(vaultName, DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);
}
