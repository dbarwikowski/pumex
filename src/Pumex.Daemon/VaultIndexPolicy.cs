using Pumex.Contracts;

namespace Pumex.Daemon;

/// <summary>
/// Per-vault rules for which files get indexed: the active extension set
/// (always Markdown, plus any extras from <c>.pumex/config.json</c>), ignore
/// globs, and an always-skip rule for dot-directories (<c>.pumex</c>,
/// <c>.git</c>, <c>.obsidian</c>, …). Recomputed whenever the config changes.
/// </summary>
public sealed class VaultIndexPolicy
{
    private readonly string _root;
    private readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase) { ".md" };
    private readonly GlobMatcher _ignore;

    public VaultIndexPolicy(string vaultRoot, VaultConfig config)
    {
        _root = vaultRoot;
        foreach (var ext in config.EffectiveFormats) _extensions.Add(ext);
        _ignore = new GlobMatcher(config.EffectiveIgnore);
    }

    public static VaultIndexPolicy Load(VaultRecord vault) =>
        new(vault.Path, VaultConfigLoader.Load(vault.Path, vault.Name));

    public IReadOnlyCollection<string> Extensions => _extensions;

    public bool ShouldIndex(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !_extensions.Contains(ext)) return false;

        var rel = RelativePath(fullPath);
        if (rel is null) return false;          // outside the vault
        if (HasDotSegment(rel)) return false;   // .git / .pumex / .obsidian / hidden
        if (_ignore.IsMatch(rel)) return false;
        return true;
    }

    /// <summary>
    /// All indexable files under the vault root. Prunes dot-directories so we
    /// don't descend into <c>.git</c> and friends; per-file ignore globs and
    /// extension checks are applied via <see cref="ShouldIndex"/>.
    /// </summary>
    public IEnumerable<string> Enumerate(Action<Exception>? onError = null)
    {
        var stack = new Stack<string>();
        stack.Push(_root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            { onError?.Invoke(ex); continue; }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (name.Length > 0 && name[0] == '.') continue;
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            { onError?.Invoke(ex); continue; }

            foreach (var file in files)
                if (ShouldIndex(file)) yield return file;
        }
    }

    private string? RelativePath(string fullPath)
    {
        var rel = Path.GetRelativePath(_root, fullPath);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return null;
        return rel;
    }

    private static bool HasDotSegment(string relativePath)
    {
        foreach (var seg in relativePath.Replace('\\', '/').Split('/'))
            if (seg.Length > 0 && seg[0] == '.') return true;
        return false;
    }
}
