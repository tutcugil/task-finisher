namespace TaskFinisher.Models.Data;

public sealed record DataGitHubIssue(
    int Number,
    string Title,
    string Body,
    string[] Labels,
    string Url
);
