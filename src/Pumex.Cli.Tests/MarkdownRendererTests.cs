using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class MarkdownRendererTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    private static Table ParseTable(string md)
    {
        var doc = Markdown.Parse(md, Pipeline);
        return Assert.IsType<Table>(doc[0]);
    }

    [Fact]
    public void Pipeline_parses_pipe_table_as_Table_block_not_paragraph()
    {
        var doc = Markdown.Parse("| A | B |\n|---|---|\n| 1 | 2 |", Pipeline);
        Assert.IsType<Table>(doc[0]);
    }

    [Fact]
    public void CellToPlainText_returns_literal_text()
    {
        var table = ParseTable("| Hello |\n|-------|\n| World |");
        var header = table.OfType<TableRow>().First(r => r.IsHeader);
        Assert.Equal("Hello", MarkdownRenderer.CellToPlainText((TableCell)header[0]));
    }

    [Fact]
    public void CellToPlainText_strips_bold()
    {
        var table = ParseTable("| **bold** |\n|----------|\n| val |");
        var header = table.OfType<TableRow>().First(r => r.IsHeader);
        Assert.Equal("bold", MarkdownRenderer.CellToPlainText((TableCell)header[0]));
    }

    [Fact]
    public void CellToPlainText_strips_italic()
    {
        var table = ParseTable("| *italic* |\n|----------|\n| val |");
        var header = table.OfType<TableRow>().First(r => r.IsHeader);
        Assert.Equal("italic", MarkdownRenderer.CellToPlainText((TableCell)header[0]));
    }

    [Fact]
    public void CellToPlainText_strips_inline_code()
    {
        var table = ParseTable("| `code` |\n|--------|\n| val |");
        var header = table.OfType<TableRow>().First(r => r.IsHeader);
        Assert.Equal("code", MarkdownRenderer.CellToPlainText((TableCell)header[0]));
    }

    [Fact]
    public void CellToPlainText_returns_plain_text_from_data_row()
    {
        var table = ParseTable("| Name |\n|------|\n| Alice |");
        var data = table.OfType<TableRow>().First(r => !r.IsHeader);
        Assert.Equal("Alice", MarkdownRenderer.CellToPlainText((TableCell)data[0]));
    }
}
