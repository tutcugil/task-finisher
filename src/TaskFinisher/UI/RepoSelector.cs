using Spectre.Console;
using TaskFinisher.Models.Data;

namespace TaskFinisher.UI;

public static class RepoSelector
{
    // Non-null wrapper so SelectionPrompt<T>'s notnull constraint is satisfied
    private sealed record Choice(DataGitHubRepo? Repo);

    private static readonly Choice ExitChoice = new(null);

    /// <summary>
    /// Shows an interactive repository picker.
    /// Returns the selected repo, or <c>null</c> if the user chose to exit.
    /// </summary>
    public static DataGitHubRepo? Select(IReadOnlyList<DataGitHubRepo> repos)
    {
        var choices = new List<Choice> { ExitChoice };
        choices.AddRange(repos.Select(r => new Choice(r)));

        AnsiConsole.WriteLine();

        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<Choice>()
                .Title("[bold white]Select a repository:[/]")
                .PageSize(18)
                .MoreChoicesText("[silver](↑↓ to reveal more)[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .UseConverter(c => c.Repo is null ? "✕  Exit" : FormatRepo(c.Repo))
                .AddChoices(choices));

        AnsiConsole.WriteLine();
        return chosen.Repo; // null = user chose Exit
    }

    private static string FormatRepo(DataGitHubRepo r)
    {
        var privacy = r.IsPrivate ? " [silver][[private]][/]" : "";

        var issues = r.OpenIssuesCount > 0
            ? $" [yellow]({r.OpenIssuesCount} open issue{(r.OpenIssuesCount == 1 ? "" : "s")})[/]"
            : " [silver](no open issues)[/]";

        var desc = !string.IsNullOrWhiteSpace(r.Description)
            ? $"  [silver]{Markup.Escape(r.Description!)}[/]"
            : "";

        return $"[white]{Markup.Escape(r.FullName)}[/]{privacy}{issues}{desc}";
    }
}
