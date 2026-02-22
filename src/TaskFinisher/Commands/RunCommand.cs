using Spectre.Console;
using Spectre.Console.Cli;
using TaskFinisher.Configuration;
using TaskFinisher.Models;
using TaskFinisher.Services;
using TaskFinisher.UI;

namespace TaskFinisher.Commands;

public sealed class RunSettings : CommandSettings
{
    [CommandOption("--token")]
    public string? Token { get; set; }

    [CommandOption("--repo")]
    public string? Repo { get; set; }

    [CommandOption("--anthropic-key")]
    public string? AnthropicKey { get; set; }

    [CommandOption("--non-interactive")]
    public bool NonInteractive { get; set; }

    [CommandOption("--model")]
    public string? Model { get; set; }

    [CommandOption("--max-iterations")]
    public int? MaxIterations { get; set; }
}

public sealed class RunCommand : AsyncCommand<RunSettings>
{
    private readonly AppSettings _settings;
    private readonly CredentialService _credentials;
    private readonly GitHubService _gitHub;
    private readonly IssueProcessor _processor;

    public RunCommand(
        AppSettings settings,
        CredentialService credentials,
        GitHubService gitHub,
        IssueProcessor processor)
    {
        _settings = settings;
        _credentials = credentials;
        _gitHub = gitHub;
        _processor = processor;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings args)
    {
        AnsiConsole.Write(new FigletText("task-finisher").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]AI-powered GitHub issue resolver[/]\n");

        try
        {
            // Phase 1: Gather credentials
            _settings.NonInteractive = args.NonInteractive;
            if (args.Model is not null) _settings.Model = args.Model;
            if (args.MaxIterations.HasValue) _settings.MaxAgentIterations = args.MaxIterations.Value;

            _credentials.Gather(_settings, args.Token, args.Repo, args.AnthropicKey);

            AnsiConsole.MarkupLine($"[bold]Repository:[/] [cyan]{_settings.Repository}[/]");
            AnsiConsole.MarkupLine($"[bold]Model:[/] [cyan]{_settings.Model}[/]\n");

            // Phase 2: Fetch open issues
            IReadOnlyList<GitHubIssue> issues = [];
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching open issues...", async ctx =>
                {
                    // GitHubService needs fresh credentials - recreate it
                    var freshGitHub = new GitHubService(_settings);
                    issues = await freshGitHub.GetOpenIssuesAsync();
                });

            if (issues.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No open issues found in the repository.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]{issues.Count}[/] open issue(s) found.\n");

            // Phase 3: Issue selection
            IReadOnlyList<GitHubIssue> selected;
            if (args.NonInteractive)
            {
                selected = issues;
                AnsiConsole.MarkupLine($"[grey]Non-interactive mode: processing all {issues.Count} issue(s).[/]\n");
            }
            else
            {
                selected = IssueSelector.Select(issues);
            }

            if (selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No issues selected. Exiting.[/]");
                return 0;
            }

            // Phase 4: Confirmation
            if (!args.NonInteractive)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]Will process {selected.Count} issue(s) and open {selected.Count} PR(s).[/]");
                var confirmed = AnsiConsole.Confirm("Proceed?");
                if (!confirmed)
                {
                    AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                    return 0;
                }
                AnsiConsole.WriteLine();
            }

            // Phase 5: Process issues
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                AnsiConsole.MarkupLine("\n[yellow]Cancellation requested...[/]");
            };

            var results = new List<IssueResult>();

            // Recreate services with final settings
            var gitHub = new GitHubService(_settings);
            var git = new GitService();
            var fsTools = new Tools.FilesystemTools();
            var claude = new ClaudeAgentService(_settings, fsTools);
            var processor = new IssueProcessor(_settings, gitHub, git, claude);

            for (int i = 0; i < selected.Count; i++)
            {
                var issue = selected[i];
                AnsiConsole.Write(new Rule($"[bold cyan]Issue #{issue.Number}[/] ({i + 1}/{selected.Count})"));
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(issue.Title)}[/]");
                AnsiConsole.WriteLine();

                IssueResult result = null!;
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"Processing issue #{issue.Number}", maxValue: 100);
                        task.StartTask();

                        var progress = new Progress<string>(msg =>
                        {
                            task.Description = Markup.Escape(msg);
                            task.Increment(2);
                        });

                        result = await processor.ProcessAsync(issue, progress, cts.Token);
                        task.Value = 100;
                    });

                if (result.Success)
                    AnsiConsole.MarkupLine($"[green]✓ PR opened:[/] {result.PullRequestUrl}");
                else
                    AnsiConsole.MarkupLine($"[red]✗ Failed:[/] {Markup.Escape(result.ErrorMessage ?? "Unknown error")}");

                results.Add(result);
                AnsiConsole.WriteLine();

                if (cts.Token.IsCancellationRequested) break;
            }

            // Phase 6: Summary
            ResultTable.Render(results);
            return results.Any(r => !r.Success) ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
