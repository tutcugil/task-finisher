using Spectre.Console;
using TaskFinisher.Models.Data;

namespace TaskFinisher.UI;

public static class ResultTable
{
    public static void Render(IReadOnlyList<DataIssueResult> results)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]#[/]")
            .AddColumn("[bold]Issue[/]")
            .AddColumn("[bold]Branch[/]")
            .AddColumn("[bold]PR[/]")
            .AddColumn("[bold]Status[/]");

        foreach (var result in results)
        {
            var status = result.Success
                ? "[green]✓ Done[/]"
                : $"[red]✗ Failed[/]";

            var pr = result.PullRequestUrl is not null
                ? $"[link={result.PullRequestUrl}]View PR[/]"
                : result.ErrorMessage is not null
                    ? $"[red]{Markup.Escape(TruncateMessage(result.ErrorMessage))}[/]"
                    : "-";

            table.AddRow(
                $"#{result.Issue.Number}",
                Markup.Escape(TruncateMessage(result.Issue.Title, 50)),
                result.BranchName is not null
                    ? $"[cyan]{Markup.Escape(result.BranchName)}[/]"
                    : "-",
                pr,
                status);
        }

        AnsiConsole.Write(table);

        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        AnsiConsole.MarkupLine(
            $"\n[bold]Summary:[/] [green]{succeeded} succeeded[/]" +
            (failed > 0 ? $", [red]{failed} failed[/]" : ""));
    }

    private static string TruncateMessage(string text, int max = 60) =>
        text.Length > max ? text[..max] + "..." : text;
}
