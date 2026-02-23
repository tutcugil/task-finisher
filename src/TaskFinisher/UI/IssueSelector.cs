using Spectre.Console;
using TaskFinisher.Models.Data;

namespace TaskFinisher.UI;

public static class IssueSelector
{
    // Non-null wrapper so MultiSelectionPrompt<T>'s notnull constraint is satisfied
    private sealed record Choice(DataGitHubIssue? Issue);

    private static readonly Choice BackChoice = new(null);

    /// <summary>
    /// Shows an interactive issue picker.
    /// Returns the selected issues, or <c>null</c> if the user chose to go back.
    /// </summary>
    public static IReadOnlyList<DataGitHubIssue>? Select(IReadOnlyList<DataGitHubIssue> issues)
    {
        if (issues.Count == 0)
            return [];

        var choices = new List<Choice> { BackChoice };
        choices.AddRange(issues.Select(i => new Choice(i)));

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<Choice>()
                .Title("[bold white]Select issues to process:[/]")
                .PageSize(15)
                .MoreChoicesText("[silver](Move up/down to reveal more)[/]")
                .InstructionsText("[silver]([deepskyblue1]Space[/] to select, [lime]Enter[/] to confirm)[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .UseConverter(c =>
                {
                    if (c.Issue is null)
                        return "← Back to previous menu";

                    var labels = c.Issue.Labels.Length > 0
                        ? $" [silver]({string.Join(", ", c.Issue.Labels)})[/]"
                        : "";
                    return $"[white]#{c.Issue.Number}[/] {Markup.Escape(c.Issue.Title)}{labels}";
                })
                .AddChoices(choices));

        // If the user selected the Back option, treat it as a cancellation
        if (selected.Any(c => c.Issue is null))
            return null;

        return selected.Select(c => c.Issue!).ToList();
    }
}
