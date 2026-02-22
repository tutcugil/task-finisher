using TaskFinisher.Models.Data;

namespace TaskFinisher.Services.Interfaces;

public interface IClaudeAgentService
{
    Task<string> RunAgentLoopAsync(
        DataGitHubIssue issue,
        string workingDirectory,
        IProgress<string> progress,
        CancellationToken ct = default);
}
