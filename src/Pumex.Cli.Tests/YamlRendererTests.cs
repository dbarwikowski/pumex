using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class YamlRendererTests
{
    private const string Mapping =
        """
        name: bob
        port: 8080
        nested:
          a: 1
        """;

    private static string SequenceOf(int n) =>
        string.Join("\n", Enumerable.Range(1, n).Select(i => $"- item{i}"));

    [Fact]
    public void Prepare_mapping_root_is_never_capped()
    {
        var (lines, notice) = YamlRenderer.Prepare(Mapping, limit: 1);

        Assert.Null(notice);                       // limit ignored for mapping roots
        Assert.Contains("name: bob", lines);
        Assert.Contains("port: 8080", lines);
    }

    [Fact]
    public void Prepare_sequence_root_within_limit_is_not_capped()
    {
        var (lines, notice) = YamlRenderer.Prepare(SequenceOf(3), limit: 100);

        Assert.Null(notice);
        Assert.Contains("- item3", lines);
    }

    [Fact]
    public void Prepare_sequence_root_over_limit_is_capped_with_notice()
    {
        var (lines, notice) = YamlRenderer.Prepare(SequenceOf(5), limit: 2);

        Assert.Equal("showing 2 of 5 elements", notice);
        Assert.Contains("- item1", lines);
        Assert.Contains("- item2", lines);
        Assert.DoesNotContain("- item3", lines);   // trimmed elements are gone
        Assert.DoesNotContain("- item5", lines);
    }

    [Fact]
    public void Prepare_keeps_nested_lines_of_kept_sequence_items()
    {
        var body =
            """
            - name: one
              tags:
                - a
            - name: two
              tags:
                - b
            - name: three
            """;

        var (lines, notice) = YamlRenderer.Prepare(body, limit: 1);

        Assert.Equal("showing 1 of 3 elements", notice);
        Assert.Contains("- name: one", lines);
        Assert.Contains("    - a", lines);             // nested content of kept item retained
        Assert.DoesNotContain("- name: two", lines);   // second top-level item dropped
    }

    [Fact]
    public void Prepare_negative_limit_means_unlimited()
    {
        var (lines, notice) = YamlRenderer.Prepare(SequenceOf(5), limit: -1);

        Assert.Null(notice);
        Assert.Contains("- item5", lines);
    }

    [Fact]
    public void Prepare_multi_document_file_is_not_capped()
    {
        var body =
            """
            - a
            - b
            ---
            - c
            - d
            """;

        var (_, notice) = YamlRenderer.Prepare(body, limit: 1);
        Assert.Null(notice);   // multi-document → no pagination
    }

    [Fact]
    public void Prepare_flow_sequence_root_is_not_capped()
    {
        // A flow sequence has no top-level "- " lines, so it is shown verbatim.
        var (lines, notice) = YamlRenderer.Prepare("[1, 2, 3, 4, 5]", limit: 2);

        Assert.Null(notice);
        Assert.Contains("[1, 2, 3, 4, 5]", lines);
    }

    [Fact]
    public void Prepare_scalar_root_is_not_capped()
    {
        var (_, notice) = YamlRenderer.Prepare("just a string", limit: 1);
        Assert.Null(notice);
    }

    [Fact]
    public void Prepare_malformed_yaml_returns_lines_without_notice_and_does_not_throw()
    {
        var (lines, notice) = YamlRenderer.Prepare("key: {unterminated", limit: 1);

        Assert.Null(notice);
        Assert.Contains("key: {unterminated", lines);
    }

    [Fact]
    public void Prepare_drops_a_single_trailing_blank_line()
    {
        var (lines, _) = YamlRenderer.Prepare("name: bob\n", limit: 100);
        Assert.Equal(["name: bob"], lines);
    }

    // --- HighlightLine: Spectre markup safety --------------------------------

    [Fact]
    public void HighlightLine_escapes_markup_brackets_in_content()
    {
        // Flow sequences contain '[' which must be escaped so Spectre does not
        // interpret it as a markup tag.
        var markup = YamlRenderer.HighlightLine("items: [a, b]");
        Assert.Contains("[[", markup);   // '[' escaped to '[['
    }

    [Fact]
    public void HighlightLine_dims_full_line_comments()
    {
        var markup = YamlRenderer.HighlightLine("# a comment");
        Assert.Contains("# a comment", markup);
        Assert.StartsWith("[", markup);   // wrapped in a style tag
    }
}
