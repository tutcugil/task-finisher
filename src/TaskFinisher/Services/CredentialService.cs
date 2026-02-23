using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using TaskFinisher.Configuration;
using TaskFinisher.Models;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class CredentialService : ICredentialService
{
    // -----------------------------------------------------------------------
    // Available models — shown in the interactive picker
    // -----------------------------------------------------------------------

    private sealed record ModelOption(string Id, string Label, string Note);

    private static readonly ModelOption[] Models =
    [
        new("claude-sonnet-4-5-20250929", "Claude Sonnet 4.5", "Smart & fast — best value  (Recommended)"),
        new("claude-sonnet-4-6",          "Claude Sonnet 4.6", "Latest Sonnet — cutting edge"),
        new("claude-opus-4-5-20251101",   "Claude Opus 4.5",   "Most capable — slower & pricier"),
        new("claude-opus-4-6",            "Claude Opus 4.6",   "Most capable, latest generation"),
        new("claude-haiku-4-5-20251001",  "Claude Haiku 4.5",  "Fastest — lightweight tasks"),
    ];

    // -----------------------------------------------------------------------
    // Credential file path
    // -----------------------------------------------------------------------

    private static string CredentialFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "task-finisher", "credentials.json");

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves GitHub token, Anthropic API key, and model choice.
    /// Resolution order: CLI arg → env var → saved file → interactive prompt.
    /// Persists newly entered values for next run.
    /// </summary>
    public void Gather(
        AppSettings settings,
        string? githubTokenArg,
        string? anthropicKeyArg,
        string? modelArg)
    {
        var saved = LoadSaved();

        settings.GitHubToken     = ResolveGitHubToken(githubTokenArg, saved, settings.NonInteractive);
        settings.AnthropicApiKey = ResolveAnthropicKey(anthropicKeyArg, saved, settings.NonInteractive);

        // ResolveModel returns null/empty when nothing was chosen (non-interactive with no saved pref),
        // in which case the appsettings.json default in settings.Model is kept untouched.
        var model = ResolveModel(modelArg, saved, settings.NonInteractive);
        if (!string.IsNullOrWhiteSpace(model))
            settings.Model = model;
    }

    /// <summary>Persists all three settings (called after re-auth at runtime).</summary>
    public static void SaveAll(string githubToken, string anthropicKey, string model)
    {
        var path = CredentialFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            JsonSerializer.Serialize(
                new SavedCredentials(githubToken, anthropicKey, model),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Persists the GitHub token only (called on re-auth).</summary>
    public static void SaveToken(string githubToken)
    {
        var e = LoadSaved();
        SaveAll(githubToken, e?.AnthropicApiKey ?? string.Empty, e?.Model ?? string.Empty);
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
    // Resolution helpers
    // -----------------------------------------------------------------------

    private static string ResolveGitHubToken(
        string? arg, SavedCredentials? saved, bool nonInteractive)
    {
        var resolved = arg ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        if (nonInteractive)
            throw new MissingCredentialsException(
                "GitHub token not found. Set GITHUB_TOKEN or pass --token.");

        if (!string.IsNullOrWhiteSpace(saved?.GitHubToken)
            && PromptUseSaved("GitHub Token", saved.GitHubToken))
            return saved.GitHubToken;

        var token = PromptSecret("GitHub Personal Access Token");
        var e     = LoadSaved();
        SaveAll(token, e?.AnthropicApiKey ?? string.Empty, e?.Model ?? string.Empty);
        AnsiConsole.MarkupLine("[silver]  GitHub token saved.[/]\n");
        return token;
    }

    private static string ResolveAnthropicKey(
        string? arg, SavedCredentials? saved, bool nonInteractive)
    {
        var resolved = arg ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        if (nonInteractive)
            throw new MissingCredentialsException(
                "Anthropic API key not found. Set ANTHROPIC_API_KEY or pass --anthropic-key.");

        if (!string.IsNullOrWhiteSpace(saved?.AnthropicApiKey)
            && PromptUseSaved("Anthropic API Key", saved.AnthropicApiKey))
            return saved.AnthropicApiKey;

        var key = PromptSecret("Anthropic API Key");
        var e   = LoadSaved();
        SaveAll(e?.GitHubToken ?? string.Empty, key, e?.Model ?? string.Empty);
        AnsiConsole.MarkupLine("[silver]  Anthropic API key saved.[/]\n");
        return key;
    }

    private static string ResolveModel(
        string? arg, SavedCredentials? saved, bool nonInteractive)
    {
        // CLI arg
        if (!string.IsNullOrWhiteSpace(arg)) return arg;

        // Env var
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        // Non-interactive: fall back to appsettings default (already set in AppSettings)
        if (nonInteractive) return string.Empty; // caller keeps the appsettings.json default

        // Saved preference: offer "keep it / change it"
        if (!string.IsNullOrWhiteSpace(saved?.Model))
        {
            var friendly = ModelLabel(saved.Model);
            AnsiConsole.WriteLine();
            var keep = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[bold white]Model:[/] [deepskyblue1]{Markup.Escape(friendly)}[/]")
                    .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                    .AddChoices(
                        $"✓  Keep  [{Markup.Escape(friendly)}]",
                        "⇄  Change model"));
            AnsiConsole.WriteLine();

            if (keep.StartsWith('✓')) return saved.Model;
        }

        // Interactive picker
        var chosen = PickModel();
        var e      = LoadSaved();
        SaveAll(e?.GitHubToken ?? string.Empty, e?.AnthropicApiKey ?? string.Empty, chosen);
        AnsiConsole.MarkupLine("[silver]  Model preference saved.[/]\n");
        return chosen;
    }

    // -----------------------------------------------------------------------
    // Model picker
    // -----------------------------------------------------------------------

    private static string PickModel()
    {
        // Build display items — index matches Models[]
        const int LabelWidth = 22;
        var modelChoices = Models
            .Select(m => $"{m.Label.PadRight(LabelWidth)}  {m.Note}")
            .ToArray();
        const string CustomChoice = "✎  Enter a custom model ID";

        AnsiConsole.WriteLine();
        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]Which model should Claude use?[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices([.. modelChoices, CustomChoice]));

        AnsiConsole.WriteLine();

        // Custom entry
        if (chosen == CustomChoice)
            return AnsiConsole.Prompt(
                new TextPrompt<string>("[bold white]Model ID:[/]")
                    .PromptStyle("deepskyblue1")
                    .Validate(v => !string.IsNullOrWhiteSpace(v)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Model ID cannot be empty.[/]")));

        // Map the chosen display string back to the model ID
        var idx = Array.IndexOf(modelChoices, chosen);
        return Models[idx].Id;
    }

    private static string ModelLabel(string modelId) =>
        Models.FirstOrDefault(m => m.Id == modelId)?.Label ?? modelId;

    // -----------------------------------------------------------------------
    // Prompts
    // -----------------------------------------------------------------------

    private static bool PromptUseSaved(string label, string token)
    {
        var masked = MaskToken(token);

        var panel = new Panel(
                $"[silver]Key[/]   [bold white]{Markup.Escape(masked)}[/]")
            .Header($"[bold deepskyblue1] Saved {label} [/]")
            .BorderColor(Color.SteelBlue1)
            .Padding(1, 0);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]What would you like to do?[/]")
                .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
                .AddChoices("✓  Use saved credential", "✎  Enter a different one"));

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
    // Persistence
    // -----------------------------------------------------------------------

    private static SavedCredentials? LoadSaved()
    {
        var path = CredentialFilePath;
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<SavedCredentials>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private sealed record SavedCredentials(
        [property: JsonPropertyName("github_token")]  string GitHubToken,
        [property: JsonPropertyName("anthropic_key")] string AnthropicApiKey,
        [property: JsonPropertyName("model")]         string Model);
}
