using System.Text.Json;
using Anthropic;
using Anthropic.Core;
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

            var response = await client.Messages.Create(
                new MessageCreateParams
                {
                    Model     = settings.Model,
                    MaxTokens = 8192,
                    System    = SystemPrompt,
                    Tools     = tools,
                    Messages  = messages
                }, cancellationToken: ct);

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
