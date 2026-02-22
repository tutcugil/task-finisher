using System.Diagnostics;

namespace TaskFinisher.Services;

public sealed class GitService
{
    public async Task CloneAsync(string repoUrl, string destinationPath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationPath);
        await RunAsync(".", $"clone {repoUrl} \"{destinationPath}\"", ct);
    }

    public async Task CheckoutBranchAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        await RunAsync(repoPath, $"checkout {branchName}", ct);
    }

    public async Task StageAllAsync(string repoPath, CancellationToken ct = default)
    {
        await RunAsync(repoPath, "add -A", ct);
    }

    public async Task CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        await RunAsync(repoPath, $"commit -m \"{EscapeArgument(message)}\"", ct);
    }

    public async Task PushAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        await RunAsync(repoPath, $"push -u origin {branchName}", ct);
    }

    public async Task<bool> HasChangesAsync(string repoPath, CancellationToken ct = default)
    {
        var output = await RunAsync(repoPath, "status --porcelain", ct);
        return !string.IsNullOrWhiteSpace(output);
    }

    private static async Task<string> RunAsync(
        string workingDirectory,
        string arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Prevent git from prompting for credentials
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            // Mask any tokens in error output before throwing
            var maskedArgs = MaskTokens(arguments);
            throw new InvalidOperationException(
                $"git {maskedArgs} failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout.Trim();
    }

    private static string MaskTokens(string text)
    {
        // Mask PAT embedded in HTTPS URLs: https://TOKEN@github.com → https://***@github.com
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"https://[^@]+@",
            "https://***@");
    }

    private static string EscapeArgument(string arg) =>
        arg.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
}
