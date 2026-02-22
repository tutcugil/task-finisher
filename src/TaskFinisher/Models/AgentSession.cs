using Anthropic.Models.Messages;

namespace TaskFinisher.Models;

public sealed class AgentSession
{
    public List<MessageParam> Messages { get; } = [];
    public int IterationCount { get; set; }
    public bool IsComplete { get; set; }
    public required string WorkingDirectory { get; init; }
}
