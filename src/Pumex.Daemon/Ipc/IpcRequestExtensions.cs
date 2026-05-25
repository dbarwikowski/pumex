using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

internal enum NoteResolutionMode
{
    /// <summary>The note must already exist in the index. Used for read/append/backlinks.</summary>
    Existing,
    /// <summary>The note may not exist yet. Bare names produce a path inside the vault root.</summary>
    Create,
}

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
    /// Resolves a user-supplied note reference to an absolute path. Three cases:
    /// <list type="number">
    ///   <item>Absolute path → canonicalised.</item>
    ///   <item>Path with directory separators → joined against the vault root if
    ///     one is in scope, otherwise treated as CWD-relative.</item>
    ///   <item>Bare name (no separator) → looked up in the vault's index by note
    ///     name. <see cref="NoteResolutionMode.Existing"/> errors on miss or
    ///     ambiguity; <see cref="NoteResolutionMode.Create"/> generates
    ///     <c>{vault}/{name}.md</c> for new notes.</item>
    /// </list>
    /// </summary>
    public static async Task<string> ResolveNotePathAsync(
        string rawPath,
        VaultRecord? vault,
        INoteRepository notes,
        NoteResolutionMode mode = NoteResolutionMode.Existing)
    {
        if (Path.IsPathFullyQualified(rawPath))
            return Path.GetFullPath(rawPath);

        var hasSeparator = rawPath.Contains('/') || rawPath.Contains('\\');
        if (hasSeparator)
        {
            var withExt = rawPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? rawPath
                : rawPath + ".md";
            return vault is not null
                ? Path.GetFullPath(Path.Combine(vault.Path, withExt))
                : Path.GetFullPath(withExt);
        }

        // Bare name — needs vault context to look up via the index.
        if (vault is null)
            throw new ArgumentException(
                $"Note name '{rawPath}' has no vault context. Pass --vault NAME, --vault-path PATH, or use a full path.");

        var name = rawPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? rawPath[..^3]
            : rawPath;

        if (mode == NoteResolutionMode.Create)
            return Path.GetFullPath(Path.Combine(vault.Path, name + ".md"));

        var matches = await notes.GetNotePathsByNameAsync(vault.Id, name);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException(
                $"No note named '{name}' in vault '{vault.Name}'."),
            _ => throw new InvalidOperationException(
                $"Ambiguous note name '{name}': {matches.Count} matches in vault '{vault.Name}'. Use a path instead."),
        };
    }

    /// <summary>
    /// Resolves the vault scope from the request. Looks for "vault" (name) or
    /// "vaultPath" args; returns null when neither is present (global scope).
    /// Throws when a name/path is given but doesn't match a registered vault.
    /// </summary>
    public static async Task<VaultRecord?> ResolveVaultAsync(this IpcRequest request, IVaultRepository vaults)
    {
        var name = request.Optional("vault");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return await vaults.GetVaultByNameAsync(name)
                ?? throw new ArgumentException($"vault not found: {name}");
        }

        var path = request.Optional("vaultPath");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var full = Path.GetFullPath(path);
            var vault = await vaults.GetVaultByPathAsync(full);
            if (vault is not null) return vault;
            if (request.Flag("vaultOptional")) return null;
            throw new ArgumentException($"vault not registered for path: {full}");
        }

        return null;
    }
}
