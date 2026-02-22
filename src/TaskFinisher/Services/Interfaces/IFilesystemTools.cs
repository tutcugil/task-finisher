using System.Text.Json;

namespace TaskFinisher.Services.Interfaces;

public interface IFilesystemTools
{
    Task<string> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> input,
        string workingDirectory,
        CancellationToken ct = default);
}
