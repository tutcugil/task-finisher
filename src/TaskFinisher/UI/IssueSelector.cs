using Spectre.Console;
using TaskFinisher.Models;

namespace TaskFinisher.UI;

public static class IssueSelector
{
    public static IReadOnlyList<GitHubIssue> Select(IReadOnlyList<GitHubIssue> issues)
    {
        if (issues.Count == 0)
            return [];

        return AnsiConsole.Prompt(
            new MultiSelectionPrompt<GitHubIssue>()
                .Title("[bold]Select issues to process:[/]")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up/down, Space to select, Enter to confirm)[/]")
                .InstructionsText("[grey]([blue]Space[/] to select, [green]Enter[/] to confirm)[/]")
                .UseConverter(i =>
                {
                    var labels = i.Labels.Length > 0 ? $" [grey]({string.Join(", ", i.Labels)})[/]" : "";
                    return $"#{i.Number} {Markup.Escape(i.Title)}{labels}";
                })
                .AddChoices(issues));
    }
}
