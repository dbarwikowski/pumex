using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class CommandHelpers
{
    internal static async Task<int> Run(Func<IpcClient, Task<int>> action)
    {
        var client = new IpcClient();
        try
        {
            return await action(client);
        }
        catch (TimeoutException)
        {
            AnsiConsole.MarkupLine("[red]Could not connect to daemon[/]. Is it running? Try [yellow]pumex daemon status[/].");
            return 2;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }
    }

    internal static int Error(string? message)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {(message ?? "unknown error").EscapeMarkup()}");
        return 1;
    }

    internal static async Task<string?> ReadStdinOrError()
    {
        if (!Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[yellow]error:[/] no content. Use [bold]--content TEXT[/] or pipe stdin.");
            return null;
        }
        return await Console.In.ReadToEndAsync();
    }

    internal static IEnumerable<string> ExpandTags(IEnumerable<string> tags) =>
        tags.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(t => t.TrimStart('#'));

    internal static IEnumerable<string> ExpandProperties(IEnumerable<string> items)
    {
        using var e = items.GetEnumerator();
        while (e.MoveNext())
        {
            var current = e.Current;
            if (current.Contains('='))
            {
                yield return current;
            }
            else if (e.MoveNext())
            {
                yield return $"{current}={e.Current}";
            }
            else
            {
                yield return current;
            }
        }
    }
}
