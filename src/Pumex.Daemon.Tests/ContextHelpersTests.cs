namespace Pumex.Daemon.Tests;

public class ContextHelpersTests
{
    // ── BuildContextQuery ─────────────────────────────────────────────────────

    [Fact]
    public void BuildContextQuery_strips_stopwords_and_ORs_remaining_terms()
    {
        var (fts, terms) = ContextRepository.BuildContextQuery("how does the indexer handle config changes");

        Assert.Equal("\"indexer\" OR \"handle\" OR \"config\" OR \"changes\"", fts);
        Assert.Equal(["indexer", "handle", "config", "changes"], terms);
    }

    [Fact]
    public void BuildContextQuery_splits_punctuation_and_quotes_each_token()
    {
        var (fts, terms) = ContextRepository.BuildContextQuery("smoke-test run");

        Assert.Equal("\"smoke\" OR \"test\" OR \"run\"", fts);
        Assert.Equal(["smoke", "test", "run"], terms);
    }

    [Fact]
    public void BuildContextQuery_keeps_words_when_query_is_all_stopwords()
    {
        var (fts, terms) = ContextRepository.BuildContextQuery("how to");

        Assert.Equal("\"how\" OR \"to\"", fts);
        Assert.Equal(["how", "to"], terms);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! --- ???")]
    public void BuildContextQuery_returns_empty_for_no_searchable_tokens(string input)
    {
        var (fts, terms) = ContextRepository.BuildContextQuery(input);

        Assert.Equal("", fts);
        Assert.Empty(terms);
    }

    [Fact]
    public void BuildContextQuery_keeps_unicode_letters()
    {
        // Polish characters must survive (InvariantGlobalization=false).
        var (fts, terms) = ContextRepository.BuildContextQuery("zażółć gęślą");

        Assert.Equal("\"zażółć\" OR \"gęślą\"", fts);
        Assert.Equal(["zażółć", "gęślą"], terms);
    }

    // ── ExtractPassage ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPassage_prepends_nearest_heading_to_matching_paragraph()
    {
        string[] body =
        [
            "# Title",
            "",
            "## Indexing section",
            "",
            "The indexer handles config changes by rescanning.",
            "It does this between files.",
            "",
            "Unrelated trailing paragraph.",
        ];

        var passage = ContextRepository.ExtractPassage(body, ["config"], 15);

        Assert.Equal(
            "Indexing section\nThe indexer handles config changes by rescanning.\nIt does this between files.",
            passage);
    }

    [Fact]
    public void ExtractPassage_returns_just_the_paragraph_when_no_heading_above()
    {
        string[] body =
        [
            "Plain opening paragraph with the keyword zebrafinch in it.",
            "Second line of the same paragraph.",
            "",
            "Another paragraph.",
        ];

        var passage = ContextRepository.ExtractPassage(body, ["zebrafinch"], 15);

        Assert.Equal(
            "Plain opening paragraph with the keyword zebrafinch in it.\nSecond line of the same paragraph.",
            passage);
    }

    [Fact]
    public void ExtractPassage_falls_back_to_first_paragraph_when_no_terms_match()
    {
        string[] body =
        [
            "First paragraph line one.",
            "First paragraph line two.",
            "",
            "Second paragraph.",
        ];

        var passage = ContextRepository.ExtractPassage(body, ["nothinghere"], 15);

        Assert.Equal("First paragraph line one.\nFirst paragraph line two.", passage);
    }

    [Fact]
    public void ExtractPassage_caps_at_max_lines()
    {
        var body = new List<string> { "## Heading", "" };
        for (var i = 0; i < 20; i++) body.Add($"keyword line {i}");

        var passage = ContextRepository.ExtractPassage(body, ["keyword"], 3);

        // Heading (marks stripped) + 2 paragraph lines = 3 total.
        Assert.Equal("Heading\nkeyword line 0\nkeyword line 1", passage);
    }

    [Fact]
    public void ExtractPassage_returns_empty_for_empty_body()
    {
        Assert.Equal("", ContextRepository.ExtractPassage([], ["x"], 15));
        Assert.Equal("", ContextRepository.ExtractPassage(["", "   "], ["x"], 15));
    }
}
