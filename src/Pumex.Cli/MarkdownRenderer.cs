using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;

namespace Pumex.Cli;

internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().Build();

    public static void Render(string markdown)
    {
        var doc = Markdown.Parse(markdown, _pipeline);
        RenderBlocks(doc, indent: 0);
    }

    private static void RenderBlocks(ContainerBlock container, int indent)
    {
        foreach (var block in container)
            RenderBlock(block, indent);
    }

    private static void RenderBlock(Block block, int indent)
    {
        var prefix = new string(' ', indent * 2);
        switch (block)
        {
            case HeadingBlock h:
                RenderHeading(h);
                break;

            case ParagraphBlock { Inline: not null } p:
                AnsiConsole.MarkupLine(prefix + InlinesToMarkup(p.Inline));
                AnsiConsole.WriteLine();
                break;

            case FencedCodeBlock fc:
                RenderCodeBlock(GetLines(fc), fc.Info);
                break;

            case CodeBlock cb:
                RenderCodeBlock(GetLines(cb), null);
                break;

            case ListBlock list:
                RenderList(list, indent);
                break;

            case ThematicBreakBlock:
                AnsiConsole.Write(new Rule());
                AnsiConsole.WriteLine();
                break;

            case QuoteBlock quote:
                RenderQuote(quote);
                break;
        }
    }

    private static void RenderHeading(HeadingBlock h)
    {
        var text = h.Inline is not null ? InlinesToMarkup(h.Inline) : string.Empty;
        switch (h.Level)
        {
            case 1:
                AnsiConsole.Write(new Rule($"[bold yellow]{text}[/]"));
                break;
            case 2:
                AnsiConsole.MarkupLine($"[bold cyan underline]{text}[/]");
                break;
            case 3:
                AnsiConsole.MarkupLine($"[bold]{text}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"[bold dim]{text}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private static void RenderCodeBlock(string code, string? language)
    {
        var header = !string.IsNullOrWhiteSpace(language)
            ? new PanelHeader($" [dim]{language.EscapeMarkup()}[/] ")
            : null;
        var panel = new Panel(new Markup($"[grey]{code.EscapeMarkup()}[/]"))
        {
            Header = header,
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0),
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static void RenderList(ListBlock list, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var n = 1;
        foreach (ListItemBlock item in list)
        {
            var bullet = list.IsOrdered ? $"{n++}." : "•";
            foreach (var block in item)
            {
                switch (block)
                {
                    case ParagraphBlock { Inline: not null } p:
                        AnsiConsole.MarkupLine($"{prefix}[dim]{bullet.EscapeMarkup()}[/] {InlinesToMarkup(p.Inline)}");
                        break;
                    case ListBlock nested:
                        RenderList(nested, indent + 1);
                        break;
                }
            }
        }
        if (indent == 0)
            AnsiConsole.WriteLine();
    }

    private static void RenderQuote(QuoteBlock quote)
    {
        var sb = new StringBuilder();
        foreach (var block in quote)
        {
            if (block is ParagraphBlock { Inline: not null } p)
                sb.AppendLine(InlinesToMarkup(p.Inline));
        }
        var panel = new Panel(new Markup($"[dim]{sb.ToString().TrimEnd()}[/]"))
        {
            Border = BoxBorder.None,
            Padding = new Padding(1, 0),
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string InlinesToMarkup(ContainerInline inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
            sb.Append(InlineToMarkup(inline));
        return sb.ToString();
    }

    private static string InlineToMarkup(Inline inline) => inline switch
    {
        LiteralInline lit => lit.Content.ToString().EscapeMarkup(),
        CodeInline code => $"[bold yellow]{code.Content.EscapeMarkup()}[/]",
        EmphasisInline { DelimiterCount: 2 } em => $"[bold]{InlinesToMarkup(em)}[/]",
        EmphasisInline em => $"[italic]{InlinesToMarkup(em)}[/]",
        LineBreakInline { IsHard: true } => "\n",
        LineBreakInline => " ",
        LinkInline link => $"[underline blue]{InlinesToMarkup(link)}[/]",
        _ => string.Empty,
    };

    private static string GetLines(LeafBlock block)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < block.Lines.Count; i++)
            sb.AppendLine(block.Lines.Lines[i].Slice.ToString());
        return sb.ToString().TrimEnd();
    }
}
