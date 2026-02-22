namespace TaskFinisher.Models.Data;

public sealed record DataIssueResult(
    DataGitHubIssue Issue,
    bool Success,
    string? BranchName,
    string? PullRequestUrl,
    string? ErrorMessage
);
