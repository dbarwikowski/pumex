namespace Pumex.Daemon;

public class WikilinkResolver
{
    private Dictionary<string, List<string>> _nameIndex = new();
    // Suffix index eliminates the O(n) SelectMany in Resolve step 1.
    // "folder/Note.md" -> ["vault/a/folder/Note.md", ...]
    private Dictionary<string, List<string>> _pathSuffixIndex = new(StringComparer.OrdinalIgnoreCase);

    public void Rebuild(IEnumerable<string> allPaths)
    {
        var paths = allPaths.ToList();
        _nameIndex = paths
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
        var name = Path.GetFileNameWithoutExtension(path);
        if (!_nameIndex.TryGetValue(name, out var list))
            _nameIndex[name] = list = new List<string>();
        if (!list.Contains(path))
            list.Add(path);
        AddToSuffixIndex(path);
    }

    public void Remove(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (_nameIndex.TryGetValue(name, out var list))
        {
            list.Remove(path);
            if (list.Count == 0) _nameIndex.Remove(name);
        }
        RemoveFromSuffixIndex(path);
    }

    public string? Resolve(string linkText, string sourcePath)
    {
        // 1. Exact path match - [[folder/Note]]
        var suffix = linkText.Replace('\\', '/') + ".md";
        if (_pathSuffixIndex.TryGetValue(suffix, out var exactMatches) && exactMatches.Count > 0)
            return exactMatches[0];

        // 2. Filename match
        if (!_nameIndex.TryGetValue(linkText, out var candidates))
            return null;

        if (candidates.Count == 1)
            return candidates[0];

        // 3. Nearest - preferuj bliżej source
        return candidates
            .OrderBy(c => PathDistance(sourcePath, c))
            .First();
    }

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

    private int PathDistance(string from, string to)
    {
        var fromParts = from.Split(Path.DirectorySeparatorChar);
        var toParts = to.Split(Path.DirectorySeparatorChar);
        var common = fromParts.Zip(toParts).TakeWhile(x => x.First == x.Second).Count();
        return (fromParts.Length - common) + (toParts.Length - common);
    }
}
