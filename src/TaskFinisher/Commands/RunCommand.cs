using Spectre.Console;
using Spectre.Console.Cli;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;
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

public sealed class RunCommand(
    AppSettings settings,
    ICredentialService credentials,
    IGitHubService gitHub,
    IIssueProcessor processor) : AsyncCommand<RunSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RunSettings args)
    {
        AnsiConsole.Write(new FigletText("task-finisher").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]AI-powered GitHub issue resolver[/]\n");

        try
        {
            ApplyArgs(args);
            credentials.Gather(settings, args.Token, args.Repo, args.AnthropicKey);

            AnsiConsole.MarkupLine($"[bold]Repository:[/] [cyan]{settings.Repository}[/]");
            AnsiConsole.MarkupLine($"[bold]Model:[/]      [cyan]{settings.Model}[/]\n");

            // Phase 1: Fetch issues
            IReadOnlyList<DataGitHubIssue> issues = [];
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching open issues...", async _ =>
                {
                    issues = await gitHub.GetOpenIssuesAsync();
                });

            if (issues.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No open issues found in the repository.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]{issues.Count}[/] open issue(s) found.\n");

            // Phase 2: Selection
            var selected = SelectIssues(args.NonInteractive, issues);
            if (selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No issues selected. Exiting.[/]");
                return 0;
            }

            // Phase 3: Confirmation
            if (!args.NonInteractive)
            {
                AnsiConsole.MarkupLine($"[bold]Will process {selected.Count} issue(s) and open {selected.Count} PR(s).[/]");
                if (!AnsiConsole.Confirm("Proceed?"))
                {
                    AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                    return 0;
                }
                AnsiConsole.WriteLine();
            }

            // Phase 4: Process
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                AnsiConsole.MarkupLine("\n[yellow]Cancellation requested...[/]");
            };

            var results = new List<DataIssueResult>();

            for (int i = 0; i < selected.Count; i++)
            {
                var issue = selected[i];
                AnsiConsole.Write(new Rule($"[bold cyan]Issue #{issue.Number}[/] ({i + 1}/{selected.Count})"));
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(issue.Title)}[/]\n");

                DataIssueResult result = null!;
                await AnsiConsole.Progress()
                    .AutoClear(false)
                    .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task     = ctx.AddTask($"Processing issue #{issue.Number}", maxValue: 100);
                        var progress = new Progress<string>(msg =>
                        {
                            task.Description = Markup.Escape(msg);
                            task.Increment(2);
                        });

                        result       = await processor.ProcessAsync(issue, progress, cts.Token);
                        task.Value   = 100;
                    });

                if (result.Success)
                    AnsiConsole.MarkupLine($"[green]✓ PR opened:[/] {result.PullRequestUrl}");
                else
                    AnsiConsole.MarkupLine($"[red]✗ Failed:[/] {Markup.Escape(result.ErrorMessage ?? "Unknown error")}");

                results.Add(result);
                AnsiConsole.WriteLine();

                if (cts.Token.IsCancellationRequested) break;
            }

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

    private void ApplyArgs(RunSettings args)
    {
        settings.NonInteractive = args.NonInteractive;
        if (args.Model is not null)          settings.Model = args.Model;
        if (args.MaxIterations.HasValue)     settings.MaxAgentIterations = args.MaxIterations.Value;
    }

    private static IReadOnlyList<DataGitHubIssue> SelectIssues(
        bool nonInteractive,
        IReadOnlyList<DataGitHubIssue> issues)
    {
        if (nonInteractive)
        {
            AnsiConsole.MarkupLine($"[grey]Non-interactive mode: processing all {issues.Count} issue(s).[/]\n");
            return issues;
        }

        return IssueSelector.Select(issues);
    }
}
