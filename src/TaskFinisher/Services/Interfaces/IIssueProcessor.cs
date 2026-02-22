using TaskFinisher.Models.Data;

namespace TaskFinisher.Services.Interfaces;

public interface IIssueProcessor
{
    Task<DataIssueResult> ProcessAsync(
        DataGitHubIssue issue,
        IProgress<string> progress,
        CancellationToken ct = default);
}
