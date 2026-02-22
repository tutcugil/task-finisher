namespace TaskFinisher.Models.Data;

/// <summary>Lightweight view of a GitHub repository used in the repo-picker UI.</summary>
public sealed record DataGitHubRepo(
    string  FullName,        // "owner/repo"
    string? Description,
    bool    IsPrivate,
    int     OpenIssuesCount);
