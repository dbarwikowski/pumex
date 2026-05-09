namespace Pumex.Daemon.Tests;

public class NoteParserTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly NoteParser _parser = new();

    public void Dispose() => _vault.Dispose();

    [Fact]
    public void Body_without_frontmatter_is_parsed_intact()
    {
        var path = _vault.WriteNote("plain.md", "# Title\n\nbody text\n");

        var doc = _parser.Parse(path);

        Assert.Empty(doc.Frontmatter);
        Assert.Equal("# Title\n\nbody text\n", doc.Content);
    }

    [Fact]
    public void Frontmatter_yaml_is_parsed_into_properties()
    {
        var path = _vault.WriteNote("note.md", "---\ntitle: Hello\nstatus: draft\n---\n\nbody\n");

        var doc = _parser.Parse(path);

        Assert.Equal("Hello", doc.Frontmatter["title"]);
        Assert.Equal("draft", doc.Frontmatter["status"]);
        Assert.Equal("body\n", doc.Content);
    }

    [Fact]
    public void Malformed_yaml_swallows_the_error_and_yields_empty_properties()
    {
        // Unbalanced YAML must not crash the daemon.
        var path = _vault.WriteNote("bad.md", "---\nthis: is: not: valid\n  - mixed\n---\n\nstill body\n");

        var doc = _parser.Parse(path);

        Assert.Empty(doc.Frontmatter);
        Assert.Equal("still body\n", doc.Content);
    }

    [Fact]
    public void Inline_hash_tags_are_extracted()
    {
        var path = _vault.WriteNote("tags.md", "Body with #foo and #bar-baz tags.\n");

        var doc = _parser.Parse(path);

        Assert.Contains("foo", doc.Tags);
        Assert.Contains("bar-baz", doc.Tags);
    }

    [Fact]
    public void Inline_tag_supports_polish_diacritics()
    {
        var path = _vault.WriteNote("pl.md", "Mam #żółć i #łąka w notatce.\n");

        var doc = _parser.Parse(path);

        Assert.Contains("żółć", doc.Tags);
        Assert.Contains("łąka", doc.Tags);
    }

    [Fact]
    public void Hash_inside_a_word_is_not_a_tag()
    {
        var path = _vault.WriteNote("nope.md", "issue#123 and url/path#anchor must not match.\n");

        var doc = _parser.Parse(path);

        Assert.Empty(doc.Tags);
    }

    [Fact]
    public void Frontmatter_tags_field_contributes_to_tag_list()
    {
        var path = _vault.WriteNote("fm.md", "---\ntags: [planning, work]\n---\n\nbody #inline\n");

        var doc = _parser.Parse(path);

        Assert.Contains("planning", doc.Tags);
        Assert.Contains("work", doc.Tags);
        Assert.Contains("inline", doc.Tags);
    }

    [Fact]
    public void Tags_are_deduped_case_insensitively()
    {
        var path = _vault.WriteNote("dup.md", "#foo and #FOO again #foo.\n");

        var doc = _parser.Parse(path);

        Assert.Single(doc.Tags);
    }

    [Fact]
    public void Wikilinks_are_extracted_with_alias_and_heading_stripped()
    {
        var path = _vault.WriteNote("links.md",
            "Refs: [[plain]] and [[alias|Display]] and [[heading#section]] and [[plain]] dup.\n");

        var doc = _parser.Parse(path);

        Assert.Equal(3, doc.OutgoingLinks.Count);
        Assert.Contains("plain", doc.OutgoingLinks);
        Assert.Contains("alias", doc.OutgoingLinks);
        Assert.Contains("heading", doc.OutgoingLinks);
    }

    [Fact]
    public void Crlf_is_normalized_to_lf_in_raw_content()
    {
        var path = _vault.WriteNote("crlf.md", "line1\r\nline2\r\n");

        var doc = _parser.Parse(path);

        Assert.DoesNotContain("\r", doc.RawContent);
        Assert.Contains("line1\nline2", doc.RawContent);
    }

    [Fact]
    public void Mtime_and_size_reflect_the_file()
    {
        var path = _vault.WriteNote("meta.md", "abc");

        var doc = _parser.Parse(path);

        Assert.Equal(3, doc.Size);
        Assert.True(doc.Mtime > 0);
    }
}
