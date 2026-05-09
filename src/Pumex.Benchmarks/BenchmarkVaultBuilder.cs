namespace Pumex.Benchmarks;

/// <summary>
/// Generates a deterministic synthetic vault on disk: <paramref name="count"/>
/// 1 KB-ish notes with frontmatter, a couple of inline tags, and a wikilink to
/// a random earlier note. Use the same seed across runs for reproducibility.
/// </summary>
internal static class BenchmarkVaultBuilder
{
    public static string Build(int count, int seed = 42)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"pumex-bench-{count}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var rng = new Random(seed);
        const int padBytes = 800; // pad each body so notes are roughly 1 KB

        for (var i = 0; i < count; i++)
        {
            var name = $"note-{i:D6}.md";
            var tagA = $"cat-{i % 50}";
            var tagB = $"src-{rng.Next(10)}";
            var linkTarget = i > 0 ? $"note-{rng.Next(i):D6}" : "self";
            var body = new string('x', padBytes);
            var content = $"""
                ---
                title: Note {i}
                tags: [{tagA}]
                ---

                # Note {i}

                Body talks about #{tagB} and refers to [[{linkTarget}]] for context.
                {body}
                """;
            File.WriteAllText(System.IO.Path.Combine(root, name), content);
        }

        return root;
    }

    public static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch { /* best-effort */ }
    }
}
