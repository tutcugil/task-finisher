using TaskFinisher.Configuration;

namespace TaskFinisher.Services.Interfaces;

public interface ICredentialService
{
    /// <summary>
    /// Resolves GitHub token, Anthropic API key and model choice from
    /// CLI args → env vars → saved file → interactive prompts.
    /// </summary>
    void Gather(AppSettings settings, string? githubTokenArg, string? anthropicKeyArg, string? modelArg);
}
