using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using TaskFinisher.Configuration;
using TaskFinisher.Models;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class CredentialService : ICredentialService
{
    private static string CredentialFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "task-finisher", "credentials.json");

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the GitHub token: CLI arg → env var → saved file → interactive prompt.
    /// Persists newly entered tokens for next run.
    /// </summary>
    public void Gather(AppSettings settings, string? tokenArg)
    {
        // Fast path: token already supplied via arg or environment
        var resolved = tokenArg ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            settings.GitHubToken = resolved;
            return;
        }

        if (settings.NonInteractive)
            throw new MissingCredentialsException(
                "GitHub token not found. Set the GITHUB_TOKEN environment variable or pass --token.");

        // Interactive: check for a saved token
        var saved = LoadSaved();
        if (saved is not null && PromptUseSavedToken(saved.GitHubToken))
        {
            settings.GitHubToken = saved.GitHubToken;
            return;
        }

        // Prompt for a new token and persist it
        settings.GitHubToken = PromptSecret("GitHub Personal Access Token");
        SaveToken(settings.GitHubToken);
        AnsiConsole.MarkupLine("[silver]  Token saved for next time.[/]\n");
    }

    /// <summary>Persists a new token (called after a re-auth during runtime).</summary>
    public static void SaveToken(string token)
    {
        var path = CredentialFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            JsonSerializer.Serialize(
                new SavedCredentials(token),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Deletes the persisted credential file. Called when --reset is passed.</summary>
    public static void ClearSaved()
    {
        var path = CredentialFilePath;
        if (!File.Exists(path)) return;
        File.Delete(path);
        AnsiConsole.MarkupLine("[silver]Saved credentials cleared.[/]\n");
    }

    // -----------------------------------------------------------------------
    // Persistence
    // -----------------------------------------------------------------------

    private static SavedCredentials? LoadSaved()
    {
        var path = CredentialFilePath;
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SavedCredentials>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Prompts
    // -----------------------------------------------------------------------

    private static bool PromptUseSavedToken(string token)
    {
        var masked = MaskToken(token);

        var panel = new Panel(
                $"[silver]Token[/]   [bold white]{Markup.Escape(masked)}[/]")
            .Header("[bold deepskyblue1] Saved Token [/]")
            .BorderColor(Color.SteelBlue1)
            .Padding(1, 0);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]What would you like to do?[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices("✓  Use saved token", "✎  Enter a different token"));

        AnsiConsole.WriteLine();
        return choice.StartsWith('✓');
    }

    private static string PromptSecret(string label) =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold white]{label}:[/]")
                .Secret()
                .PromptStyle("deepskyblue1")
                .Validate(v => !string.IsNullOrWhiteSpace(v)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]This field cannot be empty.[/]")));

    private static string MaskToken(string token) =>
        token.Length <= 8
            ? new string('*', token.Length)
            : $"{token[..4]}{"*".PadRight(8, '*')}{token[^4..]}";

    // -----------------------------------------------------------------------
    // Data
    // -----------------------------------------------------------------------

    private sealed record SavedCredentials(
        [property: JsonPropertyName("github_token")] string GitHubToken);
}
