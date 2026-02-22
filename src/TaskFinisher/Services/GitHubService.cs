using Microsoft.Extensions.Logging;
using Octokit;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class GitHubService(AppSettings settings, ILogger<GitHubService> logger) : IGitHubService
{
    // Client is created lazily so the token (populated after DI construction) is always current
    private GitHubClient Client => new GitHubClient(new ProductHeaderValue("task-finisher"))
    {
        Credentials = new Credentials(settings.GitHubToken)
    };

    public async Task<IReadOnlyList<DataGitHubRepo>> GetUserRepositoriesAsync(CancellationToken ct = default)
    {
        try
        {
            var repos = await Client.Repository.GetAllForCurrent(
                new RepositoryRequest
                {
                    Sort      = RepositorySort.Pushed,
                    Direction = SortDirection.Descending
                });

            return repos
                .Select(r => new DataGitHubRepo(
                    r.FullName,
                    r.Description,
                    r.Private,
                    r.OpenIssuesCount))
                .OrderByDescending(r => r.OpenIssuesCount > 0) // repos with issues first
                .ThenByDescending(r => r.OpenIssuesCount)
                .ToList();
        }
        catch (AuthorizationException ex)
        {
            logger.LogWarning("GitHub authentication failed fetching repos: {Message}", ex.Message);
            throw;
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("GitHub rate limit exceeded, resets at {Reset}", ex.Reset);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching user repositories");
            throw;
        }
    }

    public async Task<IReadOnlyList<DataGitHubIssue>> GetOpenIssuesAsync(CancellationToken ct = default)
    {
        try
        {
            var issues = await Client.Issue.GetAllForRepository(
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
        catch (AuthorizationException ex)
        {
            logger.LogWarning("GitHub authentication failed for {Repo}: {Message}", settings.Repository, ex.Message);
            throw;
        }
        catch (NotFoundException ex)
        {
            logger.LogWarning("Repository {Repo} not found: {Message}", settings.Repository, ex.Message);
            throw;
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("GitHub rate limit exceeded, resets at {Reset}", ex.Reset);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching issues for {Repo}", settings.Repository);
            throw;
        }
    }

    public async Task CreateBranchAsync(string branchName, CancellationToken ct = default)
    {
        try
        {
            var repo    = await Client.Repository.Get(settings.Owner, settings.Repo);
            var mainRef = await Client.Git.Reference.Get(settings.Owner, settings.Repo, $"heads/{repo.DefaultBranch}");

            await Client.Git.Reference.Create(
                settings.Owner, settings.Repo,
                new NewReference($"refs/heads/{branchName}", mainRef.Object.Sha));
        }
        catch (AuthorizationException ex)
        {
            logger.LogWarning("GitHub authentication failed creating branch {Branch}: {Message}", branchName, ex.Message);
            throw;
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
            var repo = await Client.Repository.Get(settings.Owner, settings.Repo);
            var pr   = await Client.PullRequest.Create(
                settings.Owner, settings.Repo,
                new NewPullRequest(title, branchName, repo.DefaultBranch) { Body = body });

            logger.LogInformation("PR opened: {PrUrl}", pr.HtmlUrl);
            return pr.HtmlUrl;
        }
        catch (AuthorizationException ex)
        {
            logger.LogWarning("GitHub authentication failed opening PR for {Branch}: {Message}", branchName, ex.Message);
            throw;
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
