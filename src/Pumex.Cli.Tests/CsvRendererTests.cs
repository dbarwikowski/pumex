using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class CsvRendererTests
{
    [Fact]
    public void DetectDelimiter_detects_comma()
    {
        Assert.Equal(',', CsvRenderer.DetectDelimiter("a,b,c\n1,2,3\n4,5,6"));
    }

    [Fact]
    public void DetectDelimiter_detects_tab()
    {
        Assert.Equal('\t', CsvRenderer.DetectDelimiter("a\tb\tc\n1\t2\t3"));
    }

    [Fact]
    public void DetectDelimiter_prefers_comma_when_both_match()
    {
        // Every line has exactly one comma and one tab — comma wins.
        Assert.Equal(',', CsvRenderer.DetectDelimiter("a,b\tc\n1,2\t3"));
    }

    [Fact]
    public void DetectDelimiter_returns_null_for_prose()
    {
        Assert.Null(CsvRenderer.DetectDelimiter("just some plain text\nwith no delimiters"));
    }

    [Fact]
    public void DetectDelimiter_returns_null_when_counts_are_inconsistent()
    {
        Assert.Null(CsvRenderer.DetectDelimiter("a,b,c\n1,2\n3,4,5,6"));
    }

    [Fact]
    public void DetectDelimiter_returns_null_for_empty_input()
    {
        Assert.Null(CsvRenderer.DetectDelimiter(""));
    }

    [Fact]
    public void DetectDelimiter_skips_blank_lines()
    {
        Assert.Equal(',', CsvRenderer.DetectDelimiter("a,b\n\n1,2\n\n3,4"));
    }

    [Fact]
    public void DetectDelimiter_only_samples_first_five_nonempty_lines()
    {
        // Five consistent comma lines, then a sixth with a different count.
        // Only the first five are sampled, so detection still succeeds.
        var text = "a,b\n1,2\n3,4\n5,6\n7,8\nthis line has, two, commas";
        Assert.Equal(',', CsvRenderer.DetectDelimiter(text));
    }

    [Fact]
    public void DetectDelimiter_tolerates_crlf_line_endings()
    {
        Assert.Equal(',', CsvRenderer.DetectDelimiter("a,b\r\n1,2\r\n3,4"));
    }

    [Fact]
    public void Parse_first_row_is_headers_rest_are_data()
    {
        var (headers, rows) = CsvRenderer.Parse("name,age\nAlice,30\nBob,25", ',');

        Assert.Equal(new[] { "name", "age" }, headers);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Alice", "30" }, rows[0]);
        Assert.Equal(new[] { "Bob", "25" }, rows[1]);
    }

    [Fact]
    public void Parse_respects_quoted_fields_containing_the_delimiter()
    {
        var (headers, rows) = CsvRenderer.Parse("name,note\n\"Smith, John\",hello", ',');

        Assert.Equal(new[] { "name", "note" }, headers);
        Assert.Single(rows);
        Assert.Equal("Smith, John", rows[0][0]);
        Assert.Equal("hello", rows[0][1]);
    }

    [Fact]
    public void Parse_handles_tab_delimited_input()
    {
        var (headers, rows) = CsvRenderer.Parse("a\tb\n1\t2", '\t');

        Assert.Equal(new[] { "a", "b" }, headers);
        Assert.Equal(new[] { "1", "2" }, rows[0]);
    }

    [Fact]
    public void Parse_preserves_unicode_cell_values()
    {
        var (_, rows) = CsvRenderer.Parse("city\nŁódź", ',');
        Assert.Equal("Łódź", rows[0][0]);
    }

    [Fact]
    public void Parse_returns_empty_for_empty_input()
    {
        var (headers, rows) = CsvRenderer.Parse("", ',');
        Assert.Empty(headers);
        Assert.Empty(rows);
    }

    [Fact]
    public void TruncationNotice_is_null_when_nothing_is_capped()
    {
        Assert.Null(CsvRenderer.TruncationNotice(shown: 10, total: 10));
    }

    [Fact]
    public void TruncationNotice_reports_shown_and_total_when_capped()
    {
        Assert.Equal("showing 100 of 250 rows", CsvRenderer.TruncationNotice(shown: 100, total: 250));
    }
}
