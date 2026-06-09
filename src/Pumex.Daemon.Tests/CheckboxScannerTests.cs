using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class CheckboxScannerTests
{
    private const string Sample =
        "# Tasks\n" +
        "\n" +
        "- [ ] first\n" +
        "- [x] second\n" +
        "\n" +
        "```\n" +
        "- [ ] not a real task (fenced code)\n" +
        "```\n" +
        "\n" +
        "Some prose.\n" +
        "\n" +
        "- [X] third\n";

    [Fact]
    public void Items_extracts_checkboxes_in_document_order_ignoring_code_fences()
    {
        var items = CheckboxScanner.Items(Sample);

        Assert.Equal(3, items.Count);

        Assert.Equal(1, items[0].Index);
        Assert.False(items[0].Checked);
        Assert.Equal("first", items[0].Text);

        Assert.Equal(2, items[1].Index);
        Assert.True(items[1].Checked);
        Assert.Equal("second", items[1].Text);

        // The fenced "- [ ] not a real task" line is skipped, so "third" is #3.
        Assert.Equal(3, items[2].Index);
        Assert.True(items[2].Checked);
        Assert.Equal("third", items[2].Text);
    }

    [Fact]
    public void Items_returns_empty_for_a_note_without_checkboxes()
    {
        Assert.Empty(CheckboxScanner.Items("# Heading\n\njust prose\n"));
    }

    [Fact]
    public void Toggle_checks_a_pending_item_and_leaves_the_rest_untouched()
    {
        var (content, item) = CheckboxScanner.Toggle(Sample, 1);

        Assert.Equal(1, item.Index);
        Assert.True(item.Checked);
        Assert.Equal("first", item.Text);

        Assert.Contains("- [x] first", content);
        Assert.Contains("- [x] second", content); // unchanged
        Assert.Contains("- [ ] not a real task (fenced code)", content); // fence untouched
    }

    [Fact]
    public void Toggle_unchecks_a_checked_item()
    {
        var (content, item) = CheckboxScanner.Toggle(Sample, 2);

        Assert.False(item.Checked);
        Assert.Contains("- [ ] second", content);
        Assert.Contains("- [ ] first", content); // unchanged
    }

    [Fact]
    public void Toggle_is_reversible_run_twice_returns_to_original_state()
    {
        var (once, _) = CheckboxScanner.Toggle(Sample, 1);
        var (twice, item) = CheckboxScanner.Toggle(once, 1);

        Assert.False(item.Checked);
        Assert.Contains("- [ ] first", twice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Toggle_throws_when_index_is_out_of_range(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CheckboxScanner.Toggle(Sample, index));
    }
}
