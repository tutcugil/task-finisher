using Octokit;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskFinisher.Configuration;
using TaskFinisher.Models;
using TaskFinisher.Models.Data;
using TaskFinisher.Services;
using TaskFinisher.Services.Interfaces;
using TaskFinisher.UI;

namespace TaskFinisher.Commands;

public sealed class RunSettings : CommandSettings
{
    [CommandOption("--token")]
    public string? Token { get; set; }

    [CommandOption("--anthropic-key")]
    public string? AnthropicKey { get; set; }

    [CommandOption("--model")]
    public string? Model { get; set; }

    /// <summary>Skip the interactive repo picker (useful for CI/CD).</summary>
    [CommandOption("--repo")]
    public string? Repo { get; set; }

    [CommandOption("--non-interactive")]
    public bool NonInteractive { get; set; }

    [CommandOption("--max-iterations")]
    public int? MaxIterations { get; set; }

    /// <summary>Clear saved credentials and start fresh.</summary>
    [CommandOption("--reset")]
    public bool Reset { get; set; }
}

public sealed class RunCommand(
    AppSettings        settings,
    ICredentialService credentials,
    IGitHubService     gitHub,
    IIssueProcessor    processor) : AsyncCommand<RunSettings>
{
    private enum NextAction { MoreIssues, SwitchRepo, Exit }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings args)
    {
        var header = new Panel(
                new Rows(
                    new FigletText("task").Color(Color.DeepSkyBlue1),
                    new FigletText("finisher").Color(Color.DeepSkyBlue1),
                    new Rule().RuleStyle(Style.Parse("deepskyblue1 dim")),
                    new Markup(
                        "[silver]  by[/] [bold white]Muhammet Tutcugil[/]\n" +
                        "  [deepskyblue1]https://www.tutcugil.com[/]\n" +
                        "  [white]AI-powered GitHub issue resolver[/]")))
            .Border(BoxBorder.Double)
            .BorderColor(Color.DeepSkyBlue1)
            .Padding(1, 0);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(header);
        AnsiConsole.WriteLine();

        try
        {
            ApplyArgs(args);
            credentials.Gather(settings, args.Token, args.AnthropicKey, args.Model);

            // If interactive model picker returned empty (non-interactive fallback),
            // the appsettings.json default is already in settings.Model — nothing to do.
            // If it returned a real value, apply it.
            // (The Gather method sets settings.Model directly via AppSettings ref, but
            //  ResolveModel returns empty in non-interactive mode to keep the default.)
            // Nothing extra needed here — settings.Model is already set by Gather.

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                AnsiConsole.MarkupLine("\n[yellow]Cancellation requested…[/]");
            };

            // Starting repo: from --repo arg or GITHUB_REPO env var (skips picker)
            string? currentRepo = args.Repo ?? Environment.GetEnvironmentVariable("GITHUB_REPO");

            // ----------------------------------------------------------------
            // Outer loop — repo navigation
            // ----------------------------------------------------------------
            while (!cts.IsCancellationRequested)
            {
                if (string.IsNullOrWhiteSpace(currentRepo))
                {
                    if (args.NonInteractive)
                        throw new MissingCredentialsException(
                            "Repository not found. Set the GITHUB_REPO environment variable or pass --repo.");

                    var picked = await PickRepoAsync(cts.Token);
                    if (picked is null) break; // user chose Exit from repo picker
                    currentRepo = picked;
                }

                settings.Repository = currentRepo;
                AnsiConsole.MarkupLine(
                    $"[bold white]Repository:[/] [deepskyblue1]{Markup.Escape(settings.Repository)}[/]\n");

                // ------------------------------------------------------------
                // Inner loop — work within the selected repo
                // ------------------------------------------------------------
                var action = NextAction.MoreIssues;
                while (action == NextAction.MoreIssues && !cts.IsCancellationRequested)
                    action = await RunRepoSessionAsync(args.NonInteractive, cts.Token);

                if (action == NextAction.Exit) break;

                // SwitchRepo: clear and let the outer loop pick a new one
                currentRepo = null;
            }

            AnsiConsole.MarkupLine("\n[silver]Goodbye![/]");
            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
            return 130;
        }
        catch (MissingCredentialsException ex)
        {
            // Not a crash — just missing setup. Print usage guidance and exit cleanly.
            var panel = new Panel(
                    $"[white]{Markup.Escape(ex.Message)}[/]\n\n" +
                    "[silver]Run interactively (no flags) to be guided through setup,[/]\n" +
                    "[silver]or supply credentials via environment variables:[/]\n\n" +
                    "  [deepskyblue1]GITHUB_TOKEN    [/]   [silver]your GitHub personal access token[/]\n" +
                    "  [deepskyblue1]ANTHROPIC_API_KEY[/]  [silver]your Anthropic API key[/]\n" +
                    "  [deepskyblue1]GITHUB_REPO     [/]   [silver]owner/repo  (for non-interactive mode)[/]")
                .Header("[bold yellow] Setup required [/]")
                .BorderColor(Color.Yellow)
                .Padding(1, 0);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] [white]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    // -----------------------------------------------------------------------
    // Repo picker
    // -----------------------------------------------------------------------

    private async Task<string?> PickRepoAsync(CancellationToken ct)
    {
        while (true) // retry on auth failure
        {
            try
            {
                IReadOnlyList<DataGitHubRepo> repos = [];
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("deepskyblue1"))
                    .StartAsync("[white]Fetching your repositories…[/]", async _ =>
                    {
                        repos = await gitHub.GetUserRepositoriesAsync(ct);
                    });

                var reposWithIssues = repos.Where(r => r.OpenIssuesCount > 0).ToList();

                if (reposWithIssues.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No repositories with open issues found on your account.[/]");
                    return null;
                }

                AnsiConsole.MarkupLine($"[silver]{reposWithIssues.Count} repositor{(reposWithIssues.Count == 1 ? "y" : "ies")} with open issues found.[/]");
                var picked = RepoSelector.Select(reposWithIssues);
                return picked?.FullName; // null = user chose Exit
            }
            catch (AuthorizationException)
            {
                AnsiConsole.MarkupLine("[red]✗ Authentication failed.[/] [white]Your token is invalid or has expired.[/]\n");
                settings.GitHubToken = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold white]Enter a valid GitHub Personal Access Token:[/]")
                        .Secret()
                        .PromptStyle("deepskyblue1")
                        .Validate(v => !string.IsNullOrWhiteSpace(v)
                            ? ValidationResult.Success()
                            : ValidationResult.Error("[red]Token cannot be empty.[/]")));
                CredentialService.SaveToken(settings.GitHubToken);
            }
            catch (RateLimitExceededException ex)
            {
                throw new InvalidOperationException(
                    $"GitHub API rate limit exceeded. Try again after {ex.Reset.ToLocalTime():HH:mm:ss}.");
            }
        }
    }

    // -----------------------------------------------------------------------
    // One session in the selected repo
    // -----------------------------------------------------------------------

    private async Task<NextAction> RunRepoSessionAsync(bool nonInteractive, CancellationToken ct)
    {
        // Fetch issues for the current repo (with auth-retry)
        var issues = await FetchIssuesAsync(nonInteractive, ct);

        if (issues.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No open issues found in this repository.[/]");
            return nonInteractive ? NextAction.Exit : PromptNavigation(settings.Repository);
        }

        AnsiConsole.MarkupLine($"[bold white]{issues.Count}[/] open issue(s) found.\n");

        // Action menu
        var nav = PromptRepoAction(settings.Repository, issues.Count);
        if (nav != NextAction.MoreIssues) return nav;

        // Issue selection
        var selected = IssueSelector.Select(issues);
        if (selected.Count == 0)
        {
            AnsiConsole.MarkupLine("[silver]No issues selected.[/]");
            return nonInteractive ? NextAction.Exit : PromptNavigation(settings.Repository);
        }

        // Confirm
        if (!nonInteractive)
        {
            AnsiConsole.MarkupLine(
                $"[bold white]Will process [deepskyblue1]{selected.Count}[/] issue(s) and open {selected.Count} PR(s).[/]");
            if (!AnsiConsole.Confirm("Proceed?"))
            {
                AnsiConsole.MarkupLine("[silver]Skipped.[/]\n");
                return PromptNavigation(settings.Repository);
            }
            AnsiConsole.WriteLine();
        }

        // Process each issue
        var results = new List<DataIssueResult>();
        for (int i = 0; i < selected.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var issue = selected[i];
            AnsiConsole.Write(new Rule(
                $"[bold deepskyblue1]Issue #{issue.Number}[/] [silver]({i + 1}/{selected.Count})[/]"));
            AnsiConsole.MarkupLine($"[bold white]{Markup.Escape(issue.Title)}[/]\n");

            DataIssueResult result = null!;
            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[white]Processing issue #{issue.Number}[/]", maxValue: 100);
                    var progress = new Progress<string>(msg =>
                    {
                        task.Description = $"[white]{Markup.Escape(msg)}[/]";
                        task.Increment(2);
                    });

                    result = await processor.ProcessAsync(issue, progress, ct);
                    task.Value = 100;
                });

            if (result.Success)
                AnsiConsole.MarkupLine($"[lime]✓ PR opened:[/] [white]{result.PullRequestUrl}[/]");
            else
                AnsiConsole.MarkupLine(
                    $"[red]✗ Failed:[/] [white]{Markup.Escape(result.ErrorMessage ?? "Unknown error")}[/]");

            results.Add(result);
            AnsiConsole.WriteLine();
        }

        ResultTable.Render(results);

        return nonInteractive ? NextAction.Exit : PromptNavigation(settings.Repository);
    }

    // -----------------------------------------------------------------------
    // GitHub helpers
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<DataGitHubIssue>> FetchIssuesAsync(bool nonInteractive, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                IReadOnlyList<DataGitHubIssue> issues = [];
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("deepskyblue1"))
                    .StartAsync("[white]Fetching open issues…[/]", async _ =>
                    {
                        issues = await gitHub.GetOpenIssuesAsync(ct);
                    });
                return issues;
            }
            catch (AuthorizationException)
            {
                if (nonInteractive)
                    throw new InvalidOperationException(
                        "GitHub authentication failed. Verify your token has the 'repo' scope.");

                AnsiConsole.MarkupLine(
                    "[red]✗ Authentication failed.[/] [white]Your token is invalid or has expired.[/]\n");
                settings.GitHubToken = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold white]Enter a valid GitHub Personal Access Token:[/]")
                        .Secret()
                        .PromptStyle("deepskyblue1")
                        .Validate(v => !string.IsNullOrWhiteSpace(v)
                            ? ValidationResult.Success()
                            : ValidationResult.Error("[red]Token cannot be empty.[/]")));
                CredentialService.SaveToken(settings.GitHubToken);
            }
            catch (NotFoundException)
            {
                throw new InvalidOperationException(
                    $"Repository \"{settings.Repository}\" not found or your token lacks access.");
            }
            catch (RateLimitExceededException ex)
            {
                throw new InvalidOperationException(
                    $"GitHub API rate limit exceeded. Try again after {ex.Reset.ToLocalTime():HH:mm:ss}.");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Navigation prompts
    // -----------------------------------------------------------------------

    private static NextAction PromptRepoAction(string repo, int issueCount)
    {
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold white]Found [yellow]{issueCount}[/] open issue(s) in [deepskyblue1]{Markup.Escape(repo)}[/]. What would you like to do?[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices(
                    "✓  Browse and process issues",
                    "⇄  Switch to a different repository",
                    "✕  Exit"));
        AnsiConsole.WriteLine();

        return choice[0] switch
        {
            '✓' => NextAction.MoreIssues,
            '⇄' => NextAction.SwitchRepo,
            _   => NextAction.Exit
        };
    }

    private static NextAction PromptNavigation(string repo)
    {
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]What would you like to do next?[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices(
                    $"↺  Process more issues in [deepskyblue1]{Markup.Escape(repo)}[/]",
                    "⇄  Switch to a different repository",
                    "✕  Exit"));
        AnsiConsole.WriteLine();

        return choice[0] switch
        {
            '↺' => NextAction.MoreIssues,
            '⇄' => NextAction.SwitchRepo,
            _   => NextAction.Exit
        };
    }

    // -----------------------------------------------------------------------
    // Misc
    // -----------------------------------------------------------------------

    private void ApplyArgs(RunSettings args)
    {
        // Auto-enable non-interactive mode when stdin is not a real terminal (e.g. preview runner,
        // CI/CD pipelines, piped input). This prevents Spectre.Console from crashing with
        // "Failed to read input in non-interactive mode."
        settings.NonInteractive = args.NonInteractive || Console.IsInputRedirected;

        if (args.MaxIterations.HasValue) settings.MaxAgentIterations = args.MaxIterations.Value;
        if (args.Reset) CredentialService.ClearSaved();

        // Model is resolved in Gather(); keep appsettings.json default if Gather returned empty
        // (non-interactive mode with no arg/env/saved — this is handled after Gather returns)

    }
}
