using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Services;

public sealed class ClaudeAgentService(
    AppSettings settings,
    ILogger<ClaudeAgentService> logger) : IClaudeAgentService
{
    private const string TaskCompleteMarker = "[TASK_COMPLETE]";

    // Cached absolute path to the claude binary, resolved once per process lifetime
    private static string? _claudePath;

    public async Task<string> RunAgentLoopAsync(
        DataGitHubIssue issue,
        string workingDirectory,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var prompt     = BuildPrompt(issue, workingDirectory);
        var claudePath = ResolveClaudePath();

        var psi = new ProcessStartInfo(claudePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            WorkingDirectory       = workingDirectory
        };

        // Augment PATH so git, node, npm, etc. are discoverable in the subprocess
        AugmentPath(psi);

        // Use ArgumentList for injection-proof argument passing (no manual quoting needed)
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--cwd");
        psi.ArgumentList.Add(workingDirectory);
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("Bash,Read,Write,Edit,Glob,Grep,MultiEdit");
        psi.ArgumentList.Add("--max-turns");
        psi.ArgumentList.Add(settings.MaxAgentIterations.ToString());

        logger.LogDebug("Starting claude at {Path} for issue #{Issue}", claudePath, issue.Number);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the 'claude' process.");

        string? finalResult = null;
        string? line;

        while ((line = await process.StandardOutput.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var msg = JsonSerializer.Deserialize<ClaudeStreamLine>(line);
                if (msg is null) continue;

                switch (msg.Type)
                {
                    case "tool":
                        progress.Report($"Tool: {msg.ToolName}");
                        logger.LogDebug("Tool used: {Tool}", msg.ToolName);
                        break;

                    case "assistant":
                        progress.Report("Claude is working...");
                        break;

                    case "result":
                        finalResult = msg.Result;
                        break;
                }
            }
            catch (JsonException)
            {
                // Non-JSON lines (e.g. debug output) are silently skipped
            }
        }

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogError("claude exited with code {Code}. Stderr: {Err}", process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"Claude Code exited with error (code {process.ExitCode}): {stderr.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(finalResult))
            throw new InvalidOperationException("Claude Code did not return a result.");

        var markerIndex = finalResult.IndexOf(TaskCompleteMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException(
                $"Agent did not output '{TaskCompleteMarker}'. Output: {finalResult[..Math.Min(200, finalResult.Length)]}");

        return finalResult[(markerIndex + TaskCompleteMarker.Length)..].Trim();
    }

    // -----------------------------------------------------------------------
    // Claude binary resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the absolute path to the <c>claude</c> binary.
    /// Tries the login shell first (respects nvm, volta, homebrew, etc.),
    /// then falls back to a list of well-known locations.
    /// Result is cached for the lifetime of the process.
    /// </summary>
    private static string ResolveClaudePath()
    {
        if (_claudePath is not null) return _claudePath;

        // 1. Ask the login shell — honours the user's full PATH (nvm, volta, brew, etc.)
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                using var sh = Process.Start(new ProcessStartInfo("/bin/sh")
                {
                    ArgumentList           = { "-lc", "command -v claude 2>/dev/null" },
                    RedirectStandardOutput = true,
                    UseShellExecute        = false
                });
                if (sh is not null)
                {
                    var found = sh.StandardOutput.ReadLine()?.Trim();
                    sh.WaitForExit();
                    if (!string.IsNullOrEmpty(found) && File.Exists(found))
                        return _claudePath = found;
                }
            }
            catch { /* fall through to well-known paths */ }
        }

        // 2. Check well-known install paths
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            "/usr/local/bin/claude",
            "/opt/homebrew/bin/claude",
            Path.Combine(home, ".volta",      "bin", "claude"),
            Path.Combine(home, ".npm-global", "bin", "claude"),
            Path.Combine(home, ".local",      "bin", "claude"),
            Path.Combine(home, ".nvm", "current", "bin", "claude"),
        ];

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return _claudePath = candidate;

        // 3. Claude Desktop app bundles the CLI under
        //    ~/Library/Application Support/Claude/claude-code/{version}/claude
        //    Pick the highest semver-like directory name.
        var appSupportBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Claude");

        foreach (var subDir in new[] { "claude-code", "claude-code-vm" })
        {
            var dir = Path.Combine(appSupportBase, subDir);
            if (!Directory.Exists(dir)) continue;

            var versionDirs = Directory.GetDirectories(dir)
                .OrderByDescending(d => d) // lexicographic ≈ semver for x.y.z
                .ToArray();

            foreach (var vDir in versionDirs)
            {
                var bin = Path.Combine(vDir, "claude");
                if (File.Exists(bin))
                    return _claudePath = bin;
            }
        }

        throw new InvalidOperationException(
            "Could not find the 'claude' executable. " +
            "Install Claude Code and ensure it is on your PATH:\n" +
            "  npm install -g @anthropic-ai/claude-code\n" +
            "  https://docs.anthropic.com/en/docs/claude-code");
    }

    /// <summary>
    /// Prepends common tool directories to the subprocess PATH so that git,
    /// node, npm, etc. are discoverable even when .NET doesn't inherit the
    /// full login-shell PATH.
    /// </summary>
    private static void AugmentPath(ProcessStartInfo psi)
    {
        var sep  = OperatingSystem.IsWindows() ? ';' : ':';
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // psi.Environment is a snapshot of the current process environment on first access
        var current = psi.Environment.TryGetValue("PATH", out var p) ? p
                    : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        string[] extras =
        [
            "/usr/local/bin",
            "/opt/homebrew/bin",
            "/opt/homebrew/sbin",
            Path.Combine(home, ".volta",      "bin"),
            Path.Combine(home, ".npm-global", "bin"),
            Path.Combine(home, ".local",      "bin"),
        ];

        psi.Environment["PATH"] = string.Join(sep, extras) + sep + current;
    }

    // -----------------------------------------------------------------------
    // Prompt builder
    // -----------------------------------------------------------------------

    private static string BuildPrompt(DataGitHubIssue issue, string workingDirectory) => $"""
        You are an expert software engineer implementing a GitHub issue on a code repository.

        CONTEXT:
        - Issue #{issue.Number}: {issue.Title}
        - Working directory (repository root): {workingDirectory}

        YOUR TASK:
        Explore the codebase, understand its existing patterns and conventions,
        then implement the changes required to resolve the issue.

        WORKFLOW:
        1. Use Glob or Bash to understand the project structure
        2. Read relevant files to understand the codebase conventions and style
        3. Use Grep to find related code patterns
        4. Implement the required changes
        5. When the implementation is complete, output {TaskCompleteMarker} followed by a PR description

        PR DESCRIPTION FORMAT:
        {TaskCompleteMarker}
        ## Summary
        <What was implemented and why>

        ## Changes Made
        - <file changed and what was done>

        ## Testing
        <How to verify the changes>

        RULES:
        - Only modify files within the working directory
        - Follow the existing coding style and conventions observed in the codebase
        - Write complete, working code — not pseudocode or placeholders
        - Never delete files unless explicitly required by the issue
        - If the issue is unclear, make reasonable assumptions and document them in the PR description

        ISSUE BODY:
        {issue.Body}
        """;

    // -----------------------------------------------------------------------
    // Stream-JSON deserialization model
    // -----------------------------------------------------------------------

    private sealed record ClaudeStreamLine(
        [property: JsonPropertyName("type")]      string  Type,
        [property: JsonPropertyName("tool_name")] string? ToolName,
        [property: JsonPropertyName("result")]    string? Result);
}
