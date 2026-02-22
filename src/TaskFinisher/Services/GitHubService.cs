using Microsoft.Extensions.Logging;
using Octokit;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class GitHubService(AppSettings settings, ILogger<GitHubService> logger) : IGitHubService
{
    private readonly GitHubClient _client = new GitHubClient(new ProductHeaderValue("task-finisher"))
    {
        Credentials = new Credentials(settings.GitHubToken)
    };

    public async Task<IReadOnlyList<DataGitHubIssue>> GetOpenIssuesAsync(CancellationToken ct = default)
    {
        try
        {
            var issues = await _client.Issue.GetAllForRepository(
                settings.Owner,
                settings.Repo,
                new RepositoryIssueRequest { State = ItemStateFilter.Open });

            return issues
                .Where(i => i.PullRequest == null)
                .Select(i => new DataGitHubIssue(
                    i.Number,
                    i.Title,
                    i.Body ?? string.Empty,
                    i.Labels.Select(l => l.Name).ToArray(),
                    i.HtmlUrl))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch issues for {Owner}/{Repo}", settings.Owner, settings.Repo);
            throw;
        }
    }

    public async Task CreateBranchAsync(string branchName, CancellationToken ct = default)
    {
        try
        {
            var repo       = await _client.Repository.Get(settings.Owner, settings.Repo);
            var mainRef    = await _client.Git.Reference.Get(settings.Owner, settings.Repo, $"heads/{repo.DefaultBranch}");

            await _client.Git.Reference.Create(
                settings.Owner, settings.Repo,
                new NewReference($"refs/heads/{branchName}", mainRef.Object.Sha));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create branch {Branch}", branchName);
            throw;
        }
    }

    public async Task<string> OpenPullRequestAsync(
        string branchName,
        string title,
        string body,
        CancellationToken ct = default)
    {
        try
        {
            var repo = await _client.Repository.Get(settings.Owner, settings.Repo);
            var pr   = await _client.PullRequest.Create(
                settings.Owner, settings.Repo,
                new NewPullRequest(title, branchName, repo.DefaultBranch) { Body = body });

            logger.LogInformation("PR opened: {PrUrl}", pr.HtmlUrl);
            return pr.HtmlUrl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open PR for branch {Branch}", branchName);
            throw;
        }
    }

    public string BuildCloneUrl() =>
        $"https://{settings.GitHubToken}@github.com/{settings.Owner}/{settings.Repo}.git";
}
