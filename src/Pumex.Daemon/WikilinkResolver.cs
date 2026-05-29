namespace Pumex.Daemon;

public class WikilinkResolver
{
    private const string MarkdownExtension = ".md";

    // Bare-name index for Markdown only: [[data]] resolves to data.md, never to
    // data.csv. Keyed by filename without extension.
    private Dictionary<string, List<string>> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    // Suffix index over the real filenames (with extension) of every indexed
    // file. Powers both [[folder/Note]] (bare) and [[data.csv]] (explicit ext).
    // "folder/Note.md" -> ["vault/a/folder/Note.md", ...]
    private Dictionary<string, List<string>> _pathSuffixIndex = new(StringComparer.OrdinalIgnoreCase);

    public void Rebuild(IEnumerable<string> allPaths)
    {
        var paths = allPaths.ToList();
        _nameIndex = paths
            .Where(IsMarkdown)
            .GroupBy(p => Path.GetFileNameWithoutExtension(p),
                     StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(),
                          StringComparer.OrdinalIgnoreCase);
        _pathSuffixIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            AddToSuffixIndex(path);
    }

    public void Add(string path)
    {
        if (IsMarkdown(path))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!_nameIndex.TryGetValue(name, out var list))
                _nameIndex[name] = list = new List<string>();
            if (!list.Contains(path))
                list.Add(path);
        }
        AddToSuffixIndex(path);
    }

    public void Remove(string path)
    {
        if (IsMarkdown(path))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (_nameIndex.TryGetValue(name, out var list))
            {
                list.Remove(path);
                if (list.Count == 0) _nameIndex.Remove(name);
            }
        }
        RemoveFromSuffixIndex(path);
    }

    public string? Resolve(string linkText, string sourcePath)
    {
        var normalized = linkText.Replace('\\', '/');

        // Explicit extension ([[data.csv]], [[folder/data.csv]], [[note.md]]):
        // match the real filename/path suffix exactly — no .md fallback.
        if (!string.IsNullOrEmpty(Path.GetExtension(normalized)))
            return Nearest(_pathSuffixIndex.GetValueOrDefault(normalized), sourcePath);

        // Bare name — Markdown only.
        // 1. Path-qualified [[folder/Note]] → folder/Note.md
        var suffix = normalized + MarkdownExtension;
        if (_pathSuffixIndex.TryGetValue(suffix, out var exactMatches) && exactMatches.Count > 0)
            return Nearest(exactMatches, sourcePath);

        // 2. Filename match against the Markdown-only name index.
        return Nearest(_nameIndex.GetValueOrDefault(linkText), sourcePath);
    }

    private static string? Nearest(List<string>? candidates, string sourcePath)
    {
        if (candidates is null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        return candidates.OrderBy(c => PathDistance(sourcePath, c)).First();
    }

    private static bool IsMarkdown(string path) =>
        Path.GetExtension(path).Equals(MarkdownExtension, StringComparison.OrdinalIgnoreCase);

    private void AddToSuffixIndex(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var key = string.Join('/', parts[i..]);
            if (!_pathSuffixIndex.TryGetValue(key, out var list))
                _pathSuffixIndex[key] = list = new List<string>();
            if (!list.Contains(path))
                list.Add(path);
        }
    }

    private void RemoveFromSuffixIndex(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var key = string.Join('/', parts[i..]);
            if (!_pathSuffixIndex.TryGetValue(key, out var list)) continue;
            list.Remove(path);
            if (list.Count == 0) _pathSuffixIndex.Remove(key);
        }
    }

    private static int PathDistance(string from, string to)
    {
        var fromParts = from.Split(Path.DirectorySeparatorChar);
        var toParts = to.Split(Path.DirectorySeparatorChar);
        var common = fromParts.Zip(toParts).TakeWhile(x => x.First == x.Second).Count();
        return (fromParts.Length - common) + (toParts.Length - common);
    }
}
