using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;
using TaskFinisher.Tools;

namespace TaskFinisher.Services;

public sealed class ClaudeAgentService(
    AppSettings settings,
    ILogger<ClaudeAgentService> logger) : IClaudeAgentService
{
    private const string TaskCompleteMarker = "[TASK_COMPLETE]";

    // Retry settings for rate-limit errors
    private const int    MaxRateLimitRetries  = 5;
    private const double RetryBaseDelaySeconds = 60.0;   // first wait ≈ 1 min (resets per minute)
    private const double RetryMaxDelaySeconds  = 300.0;  // cap at 5 min

    private const string SystemPrompt =
        "You are an expert software engineer. " +
        "Explore the codebase thoroughly, then implement the requested changes precisely. " +
        "When finished, output [TASK_COMPLETE] followed immediately by the PR description.";

    public async Task<string> RunAgentLoopAsync(
        DataGitHubIssue issue,
        string workingDirectory,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        var client  = new AnthropicClient(new ClientOptions { ApiKey = settings.AnthropicApiKey });
        var tools   = AgentTools.GetDefinitions();
        var runner  = new AgentTools(workingDirectory, logger);

        // Conversation history — starts with a single user message containing the prompt
        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = BuildPrompt(issue, workingDirectory) }
        };

        for (int turn = 0; turn < settings.MaxAgentIterations; turn++)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogDebug("Agent turn {Turn}/{Max}", turn + 1, settings.MaxAgentIterations);

            var response = await CallWithRateLimitRetryAsync(
                () => client.Messages.Create(
                    new MessageCreateParams
                    {
                        Model     = settings.Model,
                        MaxTokens = 8192,
                        System    = SystemPrompt,
                        Tools     = tools,
                        Messages  = messages
                    }, cancellationToken: ct),
                progress,
                ct);

            // Append the assistant's response to history.
            // ContentBlock (response type) → ContentBlockParam (request type)
            // via round-trip through the raw JsonElement each block carries.
            var assistantBlocks = response.Content
                .Select(b => JsonSerializer.Deserialize<ContentBlockParam>(b.Json)!)
                .ToList();

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantBlocks });

            // .Raw() returns the bare string ("end_turn", "tool_use", …)
            // .ToString() returns the JSON-encoded form ('"tool_use"' with quotes) — don't use it
            var stopReason = response.StopReason?.Raw() ?? string.Empty;

            // ----------------------------------------------------------------
            // end_turn: Claude finished — find the TASK_COMPLETE marker
            // ----------------------------------------------------------------
            if (stopReason == "end_turn")
            {
                foreach (var block in response.Content)
                {
                    if (!block.TryPickText(out var tb)) continue;

                    var idx = tb.Text.IndexOf(TaskCompleteMarker, StringComparison.Ordinal);
                    if (idx >= 0)
                        return tb.Text[(idx + TaskCompleteMarker.Length)..].Trim();
                }

                throw new InvalidOperationException(
                    $"Agent finished without outputting '{TaskCompleteMarker}'.");
            }

            // ----------------------------------------------------------------
            // tool_use: execute every tool call and feed results back
            // ----------------------------------------------------------------
            if (stopReason == "tool_use")
            {
                var toolResults = new List<ContentBlockParam>();

                foreach (var block in response.Content)
                {
                    if (!block.TryPickToolUse(out var toolUse)) continue;

                    progress.Report($"Tool: {toolUse.Name}");
                    logger.LogDebug("Tool call: {Name} | input: {Input}", toolUse.Name, toolUse.Input);

                    var result = await runner.ExecuteAsync(toolUse.Name, toolUse.Input, ct);

                    logger.LogDebug("Tool result ({Len} chars): {Preview}",
                        result.Length,
                        result.Length > 200 ? result[..200] + "…" : result);

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content   = result       // implicit string → ToolResultBlockParamContent
                    });
                }

                if (toolResults.Count == 0)
                    throw new InvalidOperationException(
                        "stop_reason was tool_use but no tool_use blocks were found.");

                messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
                continue;
            }

            // Any other stop reason (max_tokens, stop_sequence, …) is unexpected
            throw new InvalidOperationException(
                $"Unexpected stop_reason '{stopReason}' after {turn + 1} turns.");
        }

        throw new InvalidOperationException(
            $"Agent did not complete within {settings.MaxAgentIterations} turns.");
    }

    // -----------------------------------------------------------------------
    // Rate-limit retry
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes <paramref name="call"/> and retries up to <see cref="MaxRateLimitRetries"/> times
    /// when the API responds with a rate-limit error, using exponential back-off.
    /// </summary>
    private async Task<Message> CallWithRateLimitRetryAsync(
        Func<Task<Message>> call,
        IProgress<string> progress,
        CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (AnthropicRateLimitException ex) when (attempt < MaxRateLimitRetries)
            {
                // Exponential back-off: 60 s, 120 s, 180 s … capped at RetryMaxDelaySeconds
                var delay = TimeSpan.FromSeconds(
                    Math.Min(RetryBaseDelaySeconds * (attempt + 1), RetryMaxDelaySeconds));

                logger.LogWarning(
                    "Rate limit hit (attempt {Attempt}/{Max}). Waiting {Delay}s before retry. Detail: {Msg}",
                    attempt + 1, MaxRateLimitRetries, (int)delay.TotalSeconds, ex.Message);

                progress.Report(
                    $"Rate limit reached — waiting {(int)delay.TotalSeconds}s before retry " +
                    $"(attempt {attempt + 1}/{MaxRateLimitRetries})…");

                await Task.Delay(delay, ct);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Prompt
    // -----------------------------------------------------------------------

    private static string BuildPrompt(DataGitHubIssue issue, string workingDirectory) => $"""
        You are resolving the following GitHub issue on a code repository.

        CONTEXT:
        - Issue #{issue.Number}: {issue.Title}
        - Repository root: {workingDirectory}

        WORKFLOW:
        1. Use Glob or Bash to explore the project structure
        2. Read relevant files to understand conventions and style
        3. Use Grep to find related patterns
        4. Implement all required changes
        5. Output {TaskCompleteMarker} followed by a PR description

        PR DESCRIPTION FORMAT (immediately after {TaskCompleteMarker}):
        ## Summary
        <What was changed and why>

        ## Changes Made
        - <file: what was done>

        ## Testing
        <How to verify the changes>

        RULES:
        - Only modify files inside the repository root
        - Follow the existing coding style precisely
        - Write complete, working code — no pseudocode or placeholders
        - Never delete files unless the issue explicitly requires it

        ISSUE BODY:
        {issue.Body}
        """;
}
