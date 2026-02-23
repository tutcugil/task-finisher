namespace TaskFinisher.Configuration;

public sealed class AppSettings
{
    // Credentials - populated at runtime from env vars or interactive prompts; never written to disk
    public string GitHubToken     { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string Repository      { get; set; } = string.Empty;  // "owner/repo"

    // Runtime flags
    public bool NonInteractive { get; set; }

    // Agent configuration - overridable via appsettings.json
    public int    MaxAgentIterations { get; set; } = 50;
    public string Model              { get; set; } = "claude-sonnet-4-5-20250929";

    public string Owner => Repository.Contains('/') ? Repository.Split('/')[0] : string.Empty;
    public string Repo  => Repository.Contains('/') ? Repository.Split('/')[1] : string.Empty;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(GitHubToken)     &&
        !string.IsNullOrWhiteSpace(AnthropicApiKey) &&
        !string.IsNullOrWhiteSpace(Repository)      &&
        Repository.Contains('/');
}
