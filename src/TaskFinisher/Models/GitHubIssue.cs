namespace TaskFinisher.Models;

public sealed record GitHubIssue(
    int Number,
    string Title,
    string Body,
    string[] Labels,
    string Url
);
