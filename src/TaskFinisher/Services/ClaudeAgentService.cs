using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using TaskFinisher.Configuration;
using TaskFinisher.Models.Data;
using TaskFinisher.Services.Interfaces;
using TaskFinisher.Tools;

namespace TaskFinisher.Services;

public sealed class ClaudeAgentService(
    AppSettings settings,
    IFilesystemTools fsTools,
    ILogger<ClaudeAgentService> logger) : IClaudeAgentService
{
    private const string TaskCompleteMarker = "[TASK_COMPLETE]";

    // Client is created per-call so the API key (populated after DI construction) is always current
    private AnthropicClient CreateClient() => new() { ApiKey = settings.AnthropicApiKey };

    public async Task<string> RunAgentLoopAsync(
        DataGitHubIssue issue,
        string workingDirectory,
        IProgress<string> progress,
        CancellationToken ct = default)
    {
        try
        {
            var session        = new DataAgentSession { WorkingDirectory = workingDirectory };
            var systemPrompt   = BuildSystemPrompt(issue, workingDirectory);
            var initialMessage = BuildInitialMessage(issue);
            string prDescription = string.Empty;

            session.Messages.Add(new MessageParam
            {
                Role    = Role.User,
                Content = initialMessage
            });

            while (session.IterationCount < settings.MaxAgentIterations && !session.IsComplete)
            {
                session.IterationCount++;
                progress.Report($"Iteration {session.IterationCount}/{settings.MaxAgentIterations}...");
                ct.ThrowIfCancellationRequested();

                var response = await CreateClient().Messages.Create(new MessageCreateParams
                {
                    Model     = settings.Model,
                    MaxTokens = settings.MaxTokens,
                    System    = systemPrompt,
                    Tools     = ToolDefinitions.All,
                    Messages  = session.Messages
                }, cancellationToken: ct);

                session.Messages.Add(BuildAssistantParam(response));

                if (response.StopReason == StopReason.EndTurn)
                {
                    var text = ExtractText(response.Content);

                    if (text.Contains(TaskCompleteMarker))
                    {
                        session.IsComplete = true;
                        var markerIndex    = text.IndexOf(TaskCompleteMarker, StringComparison.Ordinal);
                        prDescription      = text[(markerIndex + TaskCompleteMarker.Length)..].Trim();
                        break;
                    }

                    progress.Report("Nudging Claude to complete...");
                    session.Messages.Add(new MessageParam
                    {
                        Role    = Role.User,
                        Content = $"Please continue. When done, output {TaskCompleteMarker} followed by the PR description."
                    });
                    continue;
                }

                if (response.StopReason == StopReason.ToolUse)
                {
                    var toolResults = new List<ContentBlockParam>();

                    foreach (var block in response.Content)
                    {
                        if (!block.TryPickToolUse(out var toolUse)) continue;

                        progress.Report($"Tool: {toolUse.Name}({GetPathPreview(toolUse.Input)})");
                        logger.LogDebug("Executing tool {ToolName}", toolUse.Name);

                        var result = await fsTools.ExecuteAsync(toolUse.Name, toolUse.Input, workingDirectory, ct);

                        toolResults.Add(new ToolResultBlockParam
                        {
                            ToolUseID = toolUse.ID,
                            Content   = result
                        });
                    }

                    session.Messages.Add(new MessageParam
                    {
                        Role    = Role.User,
                        Content = toolResults
                    });
                    continue;
                }

                if (response.StopReason == StopReason.MaxTokens)
                {
                    session.Messages.Add(new MessageParam
                    {
                        Role    = Role.User,
                        Content = "Please continue."
                    });
                }
            }

            if (!session.IsComplete)
                throw new InvalidOperationException(
                    $"Agent did not complete within {settings.MaxAgentIterations} iterations.");

            return prDescription;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "Agent loop failed for issue #{IssueNumber}", issue.Number);
            throw;
        }
    }

    private static MessageParam BuildAssistantParam(Message response)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                blocks.Add(new TextBlockParam { Text = textBlock.Text });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
            }
        }

        return new MessageParam { Role = Role.Assistant, Content = blocks };
    }

    private static string ExtractText(IReadOnlyList<ContentBlock> content)
    {
        var parts = new List<string>();
        foreach (var block in content)
        {
            if (block.TryPickText(out var textBlock))
                parts.Add(textBlock.Text);
        }
        return string.Join("\n", parts);
    }

    private static string GetPathPreview(IReadOnlyDictionary<string, System.Text.Json.JsonElement> input)
    {
        if (input.TryGetValue("path", out var pathEl))    return pathEl.GetString() ?? "";
        if (input.TryGetValue("pattern", out var patEl))  return patEl.GetString() ?? "";
        return "";
    }

    private static string BuildSystemPrompt(DataGitHubIssue issue, string workingDirectory) => $"""
        You are an expert software engineer implementing a GitHub issue on a code repository.

        CONTEXT:
        - Issue #{issue.Number}: {issue.Title}
        - Working directory (repository root): {workingDirectory}

        YOUR TASK:
        Explore the codebase using the provided tools, understand the existing patterns and conventions,
        then implement the changes required to resolve the issue.

        WORKFLOW:
        1. Start by calling list_directory(".") to understand the project structure
        2. Read relevant files to understand the codebase conventions and style
        3. Use search_in_files to find related code patterns
        4. Implement the required changes using write_file
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
        - Write complete, working code - not pseudocode or placeholders
        - Never delete files unless explicitly required by the issue
        - If the issue is unclear, make reasonable assumptions and document them in the PR description
        """;

    private static string BuildInitialMessage(DataGitHubIssue issue) => $"""
        Please implement the following GitHub issue:

        **Issue #{issue.Number}: {issue.Title}**

        {issue.Body}

        Start by exploring the project structure with list_directory("."), then read relevant files,
        and implement the necessary changes.
        """;
}
