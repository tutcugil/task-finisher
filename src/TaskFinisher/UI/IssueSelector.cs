using Spectre.Console;
using TaskFinisher.Models.Data;

namespace TaskFinisher.UI;

public static class IssueSelector
{
    public static IReadOnlyList<DataGitHubIssue> Select(IReadOnlyList<DataGitHubIssue> issues)
    {
        if (issues.Count == 0)
            return [];

        return AnsiConsole.Prompt(
            new MultiSelectionPrompt<DataGitHubIssue>()
                .Title("[bold white]Select issues to process:[/]")
                .PageSize(15)
                .MoreChoicesText("[silver](Move up/down to reveal more)[/]")
                .InstructionsText("[silver]([deepskyblue1]Space[/] to select, [lime]Enter[/] to confirm)[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .UseConverter(i =>
                {
                    var labels = i.Labels.Length > 0 ? $" [silver]({string.Join(", ", i.Labels)})[/]" : "";
                    return $"[white]#{i.Number}[/] {Markup.Escape(i.Title)}{labels}";
                })
                .AddChoices(issues));
    }
}
