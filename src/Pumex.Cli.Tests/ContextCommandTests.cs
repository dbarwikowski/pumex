using Pumex.Cli.Commands;
using Pumex.Contracts;

namespace Pumex.Cli.Tests;

public class ContextCommandTests
{
    [Fact]
    public void RenderMarkdown_emits_header_blocks_and_pointers()
    {
        var results = new List<ContextResult>
        {
            new("wiki/architecture.md", "Live config watch reloads the policy.", "architecture", 8.4, "md"),
            new("wiki/text-formats.md", "Editing the config re-scans live.", "text-formats", 5.1, "md"),
        };

        var md = ContextCommand.RenderMarkdown("indexer config changes", results);

        Assert.Equal(
            """
            # Context: indexer config changes
            2 sources · lexical

            ## wiki/architecture.md  (score 8.4)
            Live config watch reloads the policy.
            → pumex read architecture

            ## wiki/text-formats.md  (score 5.1)
            Editing the config re-scans live.
            → pumex read text-formats
            """.Replace("\r\n", "\n"),
            md.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RenderMarkdown_uses_singular_for_one_source()
    {
        var results = new List<ContextResult>
        {
            new("note.md", "Some passage.", "note", 3.0, "md"),
        };

        var md = ContextCommand.RenderMarkdown("q", results);

        Assert.Contains("1 source · lexical", md);
    }

    [Fact]
    public void RenderMarkdown_formats_score_with_invariant_culture()
    {
        var results = new List<ContextResult>
        {
            new("note.md", "Some passage.", "note", 12.5, "md"),
        };

        var md = ContextCommand.RenderMarkdown("q", results);

        Assert.Contains("(score 12.5)", md); // dot decimal regardless of OS locale
    }
}
