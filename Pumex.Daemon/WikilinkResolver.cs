namespace Pumex.Daemon;

public class WikilinkResolver
{
    private Dictionary<string, List<string>> _nameIndex = new();
    // "Note" -> ["vault/folder/Note.md", "vault/other/Note.md"]

    public void Rebuild(IEnumerable<string> allPaths)
    {
        _nameIndex = allPaths
            .GroupBy(p => Path.GetFileNameWithoutExtension(p),
                     StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(),
                          StringComparer.OrdinalIgnoreCase);
    }

    public void Add(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!_nameIndex.TryGetValue(name, out var list))
            _nameIndex[name] = list = new List<string>();
        if (!list.Contains(path))
            list.Add(path);
    }

    public void Remove(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!_nameIndex.TryGetValue(name, out var list)) return;
        list.Remove(path);
        if (list.Count == 0) _nameIndex.Remove(name);
    }

    public string? Resolve(string linkText, string sourcePath)
    {
        // 1. Exact path match - [[folder/Note]]
        if (_nameIndex.Values.SelectMany(x => x)
            .FirstOrDefault(p => p.EndsWith(linkText + ".md",
                StringComparison.OrdinalIgnoreCase)) is { } exact)
            return exact;

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

    private int PathDistance(string from, string to)
    {
        var fromParts = from.Split(Path.DirectorySeparatorChar);
        var toParts = to.Split(Path.DirectorySeparatorChar);
        var common = fromParts.Zip(toParts).TakeWhile(x => x.First == x.Second).Count();
        return (fromParts.Length - common) + (toParts.Length - common);
    }
}