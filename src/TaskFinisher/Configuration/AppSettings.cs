namespace TaskFinisher.Configuration;

public sealed class AppSettings
{
    public string GitHubToken { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;  // "owner/repo"
    public string AnthropicApiKey { get; set; } = string.Empty;
    public bool NonInteractive { get; set; }
    public int MaxAgentIterations { get; set; } = 50;
    public string Model { get; set; } = "claude-sonnet-4-6";

    public string Owner => Repository.Contains('/') ? Repository.Split('/')[0] : string.Empty;
    public string Repo => Repository.Contains('/') ? Repository.Split('/')[1] : string.Empty;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(GitHubToken) &&
        !string.IsNullOrWhiteSpace(Repository) &&
        Repository.Contains('/') &&
        !string.IsNullOrWhiteSpace(AnthropicApiKey);
}
