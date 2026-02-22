namespace TaskFinisher.Models;

public sealed record IssueResult(
    GitHubIssue Issue,
    bool Success,
    string? BranchName,
    string? PullRequestUrl,
    string? ErrorMessage
);
