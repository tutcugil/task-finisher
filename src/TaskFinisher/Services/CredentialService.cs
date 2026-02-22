using Spectre.Console;
using TaskFinisher.Configuration;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class CredentialService : ICredentialService
{
    public void Gather(AppSettings settings, string? tokenArg, string? repoArg, string? apiKeyArg)
    {
        // 1. GitHub Token: arg > env > prompt
        settings.GitHubToken = tokenArg
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException(
                    "GitHub token not found. Set the GITHUB_TOKEN environment variable or pass --token.")
                : PromptSecret("GitHub Personal Access Token"));

        // 2. Repository: arg > env > prompt
        settings.Repository = repoArg
            ?? Environment.GetEnvironmentVariable("GITHUB_REPO")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException(
                    "Repository not found. Set the GITHUB_REPO environment variable or pass --repo.")
                : PromptRepo());

        // 3. Anthropic API Key: arg > env > prompt
        settings.AnthropicApiKey = apiKeyArg
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException(
                    "Anthropic API key not found. Set the ANTHROPIC_API_KEY environment variable or pass --anthropic-key.")
                : PromptSecret("Anthropic API Key"));

        // Validate repo format for non-interactive mode (env var / arg bypasses the prompt validator)
        if (!IsValidRepo(settings.Repository))
            throw new InvalidOperationException(
                $"Invalid repository format \"{settings.Repository}\". Expected owner/repo (e.g. octocat/Hello-World).");
    }

    internal static bool IsValidRepo(string repo)
    {
        var parts = repo.Split('/');
        return parts.Length == 2 && parts.All(p => !string.IsNullOrWhiteSpace(p));
    }

    private static string PromptSecret(string label) =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]{label}:[/]")
                .Secret()
                .PromptStyle("yellow")
                .Validate(v => !string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]This field cannot be empty.[/]")));

    private static string PromptRepo() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Repository[/] [grey](owner/repo):[/]")
                .PromptStyle("cyan")
                .Validate(v => IsValidRepo(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Expected format: owner/repo (e.g. octocat/Hello-World)[/]")));
}
