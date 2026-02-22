using Spectre.Console;
using TaskFinisher.Configuration;

namespace TaskFinisher.Services;

public sealed class CredentialService
{
    public void Gather(AppSettings settings, string? tokenArg, string? repoArg, string? apiKeyArg)
    {
        // 1. GitHub Token: arg > env > prompt
        settings.GitHubToken = tokenArg
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException("GitHub token required. Set GITHUB_TOKEN env var or use --token.")
                : PromptSecret("GitHub Personal Access Token"));

        // 2. Repository: arg > env > prompt
        settings.Repository = repoArg
            ?? Environment.GetEnvironmentVariable("GITHUB_REPO")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException("Repository required. Set GITHUB_REPO env var or use --repo.")
                : PromptText("Repository (owner/repo)", "e.g. octocat/Hello-World"));

        // 3. Anthropic API Key: arg > env > prompt
        settings.AnthropicApiKey = apiKeyArg
            ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? (settings.NonInteractive
                ? throw new InvalidOperationException("Anthropic API key required. Set ANTHROPIC_API_KEY env var or use --anthropic-key.")
                : PromptSecret("Anthropic API Key"));

        if (!settings.Repository.Contains('/'))
            throw new InvalidOperationException($"Invalid repository format '{settings.Repository}'. Expected 'owner/repo'.");
    }

    private static string PromptSecret(string label) =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]{label}:[/]")
                .Secret()
                .PromptStyle("yellow"));

    private static string PromptText(string label, string hint) =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]{label}[/] [grey]({hint})[/]:")
                .PromptStyle("cyan"));
}
