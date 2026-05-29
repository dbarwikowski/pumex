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

    /// <summary>Parses the comma-separated <c>format</c> arg into normalised
    /// extensions (lowercase, no dot). Returns null when absent/empty.</summary>
    public static IReadOnlyList<string>? Formats(this IpcRequest request)
    {
        var raw = request.Optional("format");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.TrimStart('.').ToLowerInvariant())
            .Where(f => f.Length > 0)
            .Distinct()
            .ToList();
        return list.Count > 0 ? list : null;
    }

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
    /// <param name="allowNonMarkdown">
    /// When true (read/backlinks), non-Markdown references resolve: a bare
    /// <c>data.csv</c> matches by full filename and a path keeps its extension.
    /// When false (create/append/property-set/delete), non-Markdown references
    /// are rejected so those write commands stay Markdown-only.
    /// </param>
    public static async Task<string> ResolveNotePathAsync(
        string rawPath,
        VaultRecord? vault,
        INoteRepository notes,
        NoteResolutionMode mode = NoteResolutionMode.Existing,
        bool allowNonMarkdown = false)
    {
        var ext = Path.GetExtension(rawPath);
        var isMarkdownExt = ext.Equals(".md", StringComparison.OrdinalIgnoreCase);
        var hasNonMarkdownExt = ext.Length > 0 && !isMarkdownExt;

        // Guard before the absolute-path shortcut, otherwise a fully-qualified
        // non-Markdown path (e.g. C:\vault\data.csv) would bypass the check and
        // let write commands touch non-Markdown files.
        if (hasNonMarkdownExt && !allowNonMarkdown)
            throw new InvalidOperationException(
                $"'{rawPath}' is not a Markdown note; only Markdown notes can be created or modified.");

        if (Path.IsPathFullyQualified(rawPath))
            return Path.GetFullPath(rawPath);

        var hasSeparator = rawPath.Contains('/') || rawPath.Contains('\\');
        if (hasSeparator)
        {
            // Keep an explicit extension; append .md only when there is none.
            var withExt = ext.Length > 0 ? rawPath : rawPath + ".md";
            return vault is not null
                ? Path.GetFullPath(Path.Combine(vault.Path, withExt))
                : Path.GetFullPath(withExt);
        }

        // Bare name — needs vault context to look up via the index.
        if (vault is null)
            throw new ArgumentException(
                $"Note name '{rawPath}' has no vault context. Pass --vault NAME, --vault-path PATH, or use a full path.");

        // Explicit non-Markdown filename, e.g. `data.csv` — resolve by filename.
        if (hasNonMarkdownExt)
        {
            var fileMatches = await notes.GetNotePathsByFileNameAsync(vault.Id, rawPath);
            return fileMatches.Count switch
            {
                1 => fileMatches[0],
                0 => throw new FileNotFoundException($"No file named '{rawPath}' in vault '{vault.Name}'."),
                _ => throw new InvalidOperationException(
                    $"Ambiguous file name '{rawPath}': {fileMatches.Count} matches in vault '{vault.Name}'. Use a path instead."),
            };
        }

        var name = isMarkdownExt ? rawPath[..^3] : rawPath;

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
