namespace TaskFinisher.Services.Interfaces;

public interface IGitService
{
    Task CloneAsync(string repoUrl, string destinationPath, CancellationToken ct = default);
    Task CheckoutBranchAsync(string repoPath, string branchName, CancellationToken ct = default);
    Task StageAllAsync(string repoPath, CancellationToken ct = default);
    Task CommitAsync(string repoPath, string message, CancellationToken ct = default);
    Task PushAsync(string repoPath, string branchName, CancellationToken ct = default);
    Task<bool> HasChangesAsync(string repoPath, CancellationToken ct = default);
}
