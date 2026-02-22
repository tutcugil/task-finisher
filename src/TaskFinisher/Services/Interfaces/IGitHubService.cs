using TaskFinisher.Models.Data;

namespace TaskFinisher.Services.Interfaces;

public interface IGitHubService
{
    Task<IReadOnlyList<DataGitHubRepo>>     GetUserRepositoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DataGitHubIssue>>    GetOpenIssuesAsync(CancellationToken ct = default);
    Task                                    CreateBranchAsync(string branchName, CancellationToken ct = default);
    Task<string>                            OpenPullRequestAsync(string branchName, string title, string body, CancellationToken ct = default);
    string                                  BuildCloneUrl();
}
