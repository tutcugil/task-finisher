using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskFinisher.Services.Interfaces;

namespace TaskFinisher.Tools;

public sealed class FilesystemTools : IFilesystemTools
{
    public async Task<string> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> input,
        string workingDirectory,
        CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "read_file"       => ReadFile(GetString(input, "path"), workingDirectory),
                "write_file"      => WriteFile(GetString(input, "path"), GetString(input, "content"), workingDirectory),
                "list_directory"  => ListDirectory(GetString(input, "path"), workingDirectory),
                "search_in_files" => SearchInFiles(
                    GetString(input, "pattern"),
                    GetString(input, "directory"),
                    TryGetString(input, "file_glob"),
                    workingDirectory),
                "delete_file"     => DeleteFile(GetString(input, "path"), workingDirectory),
                _                 => $"Error: Unknown tool '{toolName}'"
            };
        }
        catch (SecurityException ex)
        {
            return $"Error: Security violation - {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string ReadFile(string relativePath, string baseDir)
    {
        var fullPath = SafeJoin(baseDir, relativePath);
        return File.Exists(fullPath)
            ? File.ReadAllText(fullPath)
            : $"Error: File not found: {relativePath}";
    }

    private static string WriteFile(string relativePath, string content, string baseDir)
    {
        var fullPath = SafeJoin(baseDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, content);
        return $"Successfully wrote {relativePath}";
    }

    private static string ListDirectory(string relativePath, string baseDir)
    {
        var fullPath = SafeJoin(baseDir, relativePath);
        if (!Directory.Exists(fullPath))
            return $"Error: Directory not found: {relativePath}";

        var entries = Directory.EnumerateFileSystemEntries(fullPath)
            .Take(500)
            .Select(e =>
            {
                var relPath = Path.GetRelativePath(baseDir, e).Replace('\\', '/');
                var isDir   = Directory.Exists(e);
                return new { name = Path.GetFileName(e), type = isDir ? "directory" : "file", path = relPath };
            })
            .ToList();

        return JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SearchInFiles(string pattern, string directory, string? fileGlob, string baseDir)
    {
        var fullDir = SafeJoin(baseDir, directory);
        if (!Directory.Exists(fullDir))
            return $"Error: Directory not found: {directory}";

        var glob    = fileGlob ?? "*";
        var files   = Directory.EnumerateFiles(fullDir, glob, SearchOption.AllDirectories);
        var results = new StringBuilder();
        int matchCount = 0;

        foreach (var file in files)
        {
            if (matchCount >= 200) break;

            var lines   = File.ReadAllLines(file);
            var relFile = Path.GetRelativePath(baseDir, file).Replace('\\', '/');

            for (int i = 0; i < lines.Length && matchCount < 200; i++)
            {
                if (!Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase)) continue;
                results.AppendLine($"{relFile}:{i + 1}: {lines[i].Trim()}");
                matchCount++;
            }
        }

        return matchCount == 0
            ? $"No matches found for '{pattern}'"
            : results.ToString();
    }

    private static string DeleteFile(string relativePath, string baseDir)
    {
        var fullPath = SafeJoin(baseDir, relativePath);
        if (!File.Exists(fullPath))
            return $"Error: File not found: {relativePath}";

        File.Delete(fullPath);
        return $"Deleted {relativePath}";
    }

    private static string SafeJoin(string baseDir, string relativePath)
    {
        var fullBase = Path.GetFullPath(baseDir);
        var combined = Path.GetFullPath(Path.Combine(baseDir, relativePath));
        if (!combined.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException($"Path traversal attempt detected: {relativePath}");
        return combined;
    }

    private static string GetString(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var el))
            throw new ArgumentException($"Missing required parameter: {key}");
        return el.GetString() ?? throw new ArgumentException($"Parameter '{key}' must be a string");
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) ? el.GetString() : null;
}
