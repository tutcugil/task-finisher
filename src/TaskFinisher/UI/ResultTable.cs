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
            .BorderColor(Color.SteelBlue1)
            .AddColumn(new TableColumn("[bold white]#[/]").Centered())
            .AddColumn("[bold white]Issue[/]")
            .AddColumn("[bold white]Branch[/]")
            .AddColumn("[bold white]PR[/]")
            .AddColumn(new TableColumn("[bold white]Status[/]").Centered());

        foreach (var result in results)
        {
            var status = result.Success
                ? "[lime]✓ Done[/]"
                : "[red]✗ Failed[/]";

            var pr = result.PullRequestUrl is not null
                ? $"[link={result.PullRequestUrl}][deepskyblue1]View PR[/][/]"
                : result.ErrorMessage is not null
                    ? $"[red]{Markup.Escape(TruncateMessage(result.ErrorMessage))}[/]"
                    : "[silver]-[/]";

            table.AddRow(
                $"[white]#{result.Issue.Number}[/]",
                $"[white]{Markup.Escape(TruncateMessage(result.Issue.Title, 50))}[/]",
                result.BranchName is not null
                    ? $"[deepskyblue1]{Markup.Escape(result.BranchName)}[/]"
                    : "[silver]-[/]",
                pr,
                status);
        }

        AnsiConsole.Write(table);

        var succeeded = results.Count(r => r.Success);
        var failed    = results.Count(r => !r.Success);

        AnsiConsole.MarkupLine(
            $"\n[bold white]Summary:[/] [lime]{succeeded} succeeded[/]" +
            (failed > 0 ? $", [red]{failed} failed[/]" : ""));
    }

    private static string TruncateMessage(string text, int max = 60) =>
        text.Length > max ? text[..max] + "…" : text;
}
