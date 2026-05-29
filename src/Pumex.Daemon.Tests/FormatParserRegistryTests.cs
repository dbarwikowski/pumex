namespace Pumex.Daemon.Tests;

public class FormatParserRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pumex-fmt-" + Guid.NewGuid().ToString("N"));

    public FormatParserRegistryTests() => Directory.CreateDirectory(_dir);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Markdown_files_go_through_the_markdown_parser()
    {
        var registry = FormatParserRegistry.Default();
        var path = Write("note.md", "---\ntitle: T\n---\n\nbody #tag with [[link]]\n");

        var doc = registry.Parse(path);

        Assert.Contains("tag", doc.Tags);
        Assert.Contains("link", doc.OutgoingLinks);
        Assert.True(doc.Frontmatter.ContainsKey("title"));
    }

    [Fact]
    public void Unknown_extensions_fall_back_to_raw_text_with_no_structure()
    {
        var registry = FormatParserRegistry.Default();
        var path = Write("data.csv", "a,b\n#notatag,[[notalink]]\n");

        var doc = registry.Parse(path);

        Assert.Empty(doc.Tags);
        Assert.Empty(doc.OutgoingLinks);
        Assert.Empty(doc.Frontmatter);
        Assert.Contains("notatag", doc.Content);   // whole file is the FTS body
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
