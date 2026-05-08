using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

internal static class IpcRequestExtensions
{
    public static string Require(this IpcRequest request, string name)
    {
        if (!request.Args.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required");
        return value;
    }

    public static string? Optional(this IpcRequest request, string name) =>
        request.Args.TryGetValue(name, out var value) ? value : null;

    public static bool Flag(this IpcRequest request, string name) =>
        request.Args.TryGetValue(name, out var value) && value is "1" or "true" or "yes";

    /// <summary>
    /// Resolves a user-supplied note path. Absolute paths are returned canonical;
    /// relative paths resolve against <paramref name="vault"/>'s root when one is
    /// in scope, otherwise fall back to the daemon's CWD (rarely meaningful — the
    /// CLI usually canonicalises before sending).
    /// </summary>
    public static string ResolveNotePath(string rawPath, VaultRecord? vault)
    {
        if (Path.IsPathFullyQualified(rawPath))
            return Path.GetFullPath(rawPath);
        if (vault is not null)
            return Path.GetFullPath(Path.Combine(vault.Path, rawPath));
        return Path.GetFullPath(rawPath);
    }

    /// <summary>
    /// Resolves the vault scope from the request. Looks for "vault" (name) or
    /// "vaultPath" args; returns null when neither is present (global scope).
    /// Throws when a name/path is given but doesn't match a registered vault.
    /// </summary>
    public static async Task<VaultRecord?> ResolveVaultAsync(this IpcRequest request, IndexDb db)
    {
        var name = request.Optional("vault");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return await db.GetVaultByNameAsync(name)
                ?? throw new ArgumentException($"vault not found: {name}");
        }

        var path = request.Optional("vaultPath");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var full = Path.GetFullPath(path);
            var vault = await db.GetVaultByPathAsync(full);
            if (vault is not null) return vault;
            if (request.Flag("vaultOptional")) return null;
            throw new ArgumentException($"vault not registered for path: {full}");
        }

        return null;
    }
}
