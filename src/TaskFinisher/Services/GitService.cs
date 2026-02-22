using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class GitService(ILogger<GitService> logger) : IGitService
{
    public async Task CloneAsync(string repoUrl, string destinationPath, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationPath);
            await RunAsync(".", $"clone {repoUrl} \"{destinationPath}\"", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git clone failed for destination {Destination}", destinationPath);
            throw;
        }
    }

    public async Task CheckoutBranchAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        try
        {
            await RunAsync(repoPath, $"checkout {branchName}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git checkout failed for branch {Branch}", branchName);
            throw;
        }
    }

    public async Task StageAllAsync(string repoPath, CancellationToken ct = default)
    {
        try
        {
            await RunAsync(repoPath, "add -A", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git add failed in {RepoPath}", repoPath);
            throw;
        }
    }

    public async Task CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        try
        {
            await RunAsync(repoPath, $"commit -m \"{EscapeArgument(message)}\"", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git commit failed in {RepoPath}", repoPath);
            throw;
        }
    }

    public async Task PushAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        try
        {
            await RunAsync(repoPath, $"push -u origin {branchName}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git push failed for branch {Branch}", branchName);
            throw;
        }
    }

    public async Task<bool> HasChangesAsync(string repoPath, CancellationToken ct = default)
    {
        try
        {
            var output = await RunAsync(repoPath, "status --porcelain", ct);
            return !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "git status failed in {RepoPath}", repoPath);
            throw;
        }
    }

    private static async Task<string> RunAsync(string workingDirectory, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"]         = "echo";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {MaskTokens(arguments)} failed (exit {process.ExitCode}): {stderr.Trim()}");

        return stdout.Trim();
    }

    private static string MaskTokens(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"https://[^@]+@", "https://***@");

    private static string EscapeArgument(string arg) =>
        arg.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
}
