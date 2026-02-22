using TaskFinisher.Configuration;

namespace TaskFinisher.Services.Interfaces;

public interface ICredentialService
{
    void Gather(AppSettings settings, string? tokenArg, string? repoArg, string? apiKeyArg);
}
