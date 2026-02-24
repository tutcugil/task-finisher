using Spectre.Console;
using TaskFinisher.Models.Data;

namespace TaskFinisher.UI;

public static class RepoSelector
{
    // Non-null wrapper so SelectionPrompt<T>'s notnull constraint is satisfied
    private sealed record Choice(DataGitHubRepo? Repo, bool ShowAll = false);

    private static readonly Choice ExitChoice    = new(null, ShowAll: false);
    private static readonly Choice ShowAllChoice = new(null, ShowAll: true);

    /// <summary>
    /// Shows an interactive repository picker.
    /// Returns the selected repo's full name, or <c>null</c> if the user chose to exit.
    /// When <paramref name="showAllOption"/> is <c>true</c> an extra
    /// "Show all other repositories" entry is appended; if chosen, the method
    /// returns <see cref="ShowAllRepositories"/> so the caller can re-invoke
    /// with the complete list.
    /// </summary>
    public static RepoSelectionResult Select(
        IReadOnlyList<DataGitHubRepo> repos,
        bool showAllOption = false)
    {
        var choices = new List<Choice> { ExitChoice };
        choices.AddRange(repos.Select(r => new Choice(r)));
        if (showAllOption)
            choices.Add(ShowAllChoice);

        AnsiConsole.WriteLine();

        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<Choice>()
                .Title("[bold white]Select a repository:[/]")
                .PageSize(18)
                .MoreChoicesText("[silver](↑↓ to reveal more)[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .UseConverter(c =>
                {
                    if (c.ShowAll) return "☰  Show all other repositories";
                    return c.Repo is null ? "✕  Exit" : FormatRepo(c.Repo);
                })
                .AddChoices(choices));

        AnsiConsole.WriteLine();

        if (chosen.ShowAll)
            return RepoSelectionResult.ShowAllRepositories;

        return chosen.Repo is null
            ? RepoSelectionResult.Exit
            : RepoSelectionResult.Selected(chosen.Repo.FullName);
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

/// <summary>Outcome of <see cref="RepoSelector.Select"/>.</summary>
public sealed class RepoSelectionResult
{
    public enum Kind { Selected, Exit, ShowAll }

    public Kind         ResultKind { get; }
    public string?      FullName   { get; }

    private RepoSelectionResult(Kind kind, string? fullName = null)
    {
        ResultKind = kind;
        FullName   = fullName;
    }

    public static readonly RepoSelectionResult Exit                = new(Kind.Exit);
    public static readonly RepoSelectionResult ShowAllRepositories = new(Kind.ShowAll);

    public static RepoSelectionResult Selected(string fullName) =>
        new(Kind.Selected, fullName);
}
