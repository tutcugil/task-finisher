using Octokit;
using TaskFinisher.Configuration;
using TaskFinisher.Models;

namespace TaskFinisher.Services;

public sealed class GitHubService
{
    private readonly GitHubClient _client;
    private readonly AppSettings _settings;

    public GitHubService(AppSettings settings)
    {
        _settings = settings;
        _client = new GitHubClient(new ProductHeaderValue("task-finisher"))
        {
            Credentials = new Credentials(settings.GitHubToken)
        };
    }

    public async Task<IReadOnlyList<Models.GitHubIssue>> GetOpenIssuesAsync(CancellationToken ct = default)
    {
        var issues = await _client.Issue.GetAllForRepository(
            _settings.Owner,
            _settings.Repo,
            new RepositoryIssueRequest { State = ItemStateFilter.Open });

        return issues
            .Where(i => i.PullRequest == null)  // Exclude PRs (GitHub returns them as issues too)
            .Select(i => new Models.GitHubIssue(
                i.Number,
                i.Title,
                i.Body ?? string.Empty,
                i.Labels.Select(l => l.Name).ToArray(),
                i.HtmlUrl))
            .ToList();
    }

    public async Task CreateBranchAsync(string branchName, CancellationToken ct = default)
    {
        var repo = await _client.Repository.Get(_settings.Owner, _settings.Repo);
        var defaultBranch = repo.DefaultBranch;

        var mainRef = await _client.Git.Reference.Get(
            _settings.Owner, _settings.Repo, $"heads/{defaultBranch}");

        await _client.Git.Reference.Create(
            _settings.Owner, _settings.Repo,
            new NewReference($"refs/heads/{branchName}", mainRef.Object.Sha));
    }

    public async Task<string> OpenPullRequestAsync(
        string branchName,
        string title,
        string body,
        CancellationToken ct = default)
    {
        var repo = await _client.Repository.Get(_settings.Owner, _settings.Repo);
        var pr = await _client.PullRequest.Create(
            _settings.Owner, _settings.Repo,
            new NewPullRequest(title, branchName, repo.DefaultBranch) { Body = body });

        return pr.HtmlUrl;
    }

    public string BuildCloneUrl() =>
        $"https://{_settings.GitHubToken}@github.com/{_settings.Owner}/{_settings.Repo}.git";
}
