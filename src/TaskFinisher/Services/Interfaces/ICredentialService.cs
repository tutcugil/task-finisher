using TaskFinisher.Configuration;

namespace TaskFinisher.Services.Interfaces;

public interface ICredentialService
{
    /// <summary>Resolves the GitHub token from arg → env → saved file → interactive prompt.</summary>
    void Gather(AppSettings settings, string? tokenArg);
}
