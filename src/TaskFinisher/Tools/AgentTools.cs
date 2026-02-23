using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace TaskFinisher.Tools;

/// <summary>
/// Provides tool definitions (for the Anthropic API) and executes tool calls
/// that Claude issues during the agentic loop.
/// </summary>
public sealed class AgentTools(string workingDirectory, ILogger logger)
{
    private static readonly IReadOnlyList<ToolUnion> _definitions = BuildDefinitions();

    /// <summary>Returns the tool definitions to pass to the Anthropic Messages API.</summary>
    public static IReadOnlyList<ToolUnion> GetDefinitions() => _definitions;

    // -----------------------------------------------------------------------
    // Execution dispatcher
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a tool call issued by Claude and returns the result as a string.
    /// All file paths are sandboxed to <see cref="workingDirectory"/>.
    /// </summary>
    public async Task<string> ExecuteAsync(
        string name,
        IReadOnlyDictionary<string, JsonElement> input,
        CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "Read"      => ExecuteRead(input),
                "Write"     => ExecuteWrite(input),
                "Edit"      => ExecuteEdit(input),
                "MultiEdit" => ExecuteMultiEdit(input),
                "Glob"      => ExecuteGlob(input),
                "Grep"      => ExecuteGrep(input),
                "Bash"      => await ExecuteBashAsync(input, ct),
                _           => $"Unknown tool: {name}"
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning("Tool '{Name}' failed: {Error}", name, ex.Message);
            return $"Error: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------------
    // Read — return file contents
    // -----------------------------------------------------------------------

    private string ExecuteRead(IReadOnlyDictionary<string, JsonElement> input)
    {
        var path = ResolvePath(Str(input, "path"));
        if (!File.Exists(path)) return $"File not found: {path}";

        var content = File.ReadAllText(path);
        var lines   = content.Split('\n');

        // Prefix with line numbers for easier referencing by Claude
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
            sb.Append(i + 1).Append('\t').AppendLine(lines[i]);

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Write — create or overwrite a file
    // -----------------------------------------------------------------------

    private string ExecuteWrite(IReadOnlyDictionary<string, JsonElement> input)
    {
        var path    = ResolvePath(Str(input, "path"));
        var content = Str(input, "content");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return $"Wrote {content.Length} chars to {Path.GetRelativePath(workingDirectory, path)}";
    }

    // -----------------------------------------------------------------------
    // Edit — replace one exact string in a file
    // -----------------------------------------------------------------------

    private string ExecuteEdit(IReadOnlyDictionary<string, JsonElement> input)
    {
        var path      = ResolvePath(Str(input, "path"));
        var oldString = Str(input, "old_string");
        var newString = Str(input, "new_string");

        if (!File.Exists(path)) return $"File not found: {path}";

        var original = File.ReadAllText(path);
        if (!original.Contains(oldString, StringComparison.Ordinal))
            return $"old_string not found in {Path.GetRelativePath(workingDirectory, path)}";

        File.WriteAllText(path, original.Replace(oldString, newString, StringComparison.Ordinal));
        return "Edit applied.";
    }

    // -----------------------------------------------------------------------
    // MultiEdit — apply multiple string replacements to one file
    // -----------------------------------------------------------------------

    private string ExecuteMultiEdit(IReadOnlyDictionary<string, JsonElement> input)
    {
        var path = ResolvePath(Str(input, "path"));
        if (!File.Exists(path)) return $"File not found: {path}";

        if (!input.TryGetValue("edits", out var editsEl)
            || editsEl.ValueKind != JsonValueKind.Array)
            return "Missing or invalid 'edits' array.";

        var content = File.ReadAllText(path);
        var applied = 0;

        foreach (var editEl in editsEl.EnumerateArray())
        {
            var oldStr = editEl.GetProperty("old_string").GetString() ?? "";
            var newStr = editEl.GetProperty("new_string").GetString() ?? "";
            if (!content.Contains(oldStr, StringComparison.Ordinal)) continue;
            content = content.Replace(oldStr, newStr, StringComparison.Ordinal);
            applied++;
        }

        File.WriteAllText(path, content);
        return $"{applied} edit(s) applied.";
    }

    // -----------------------------------------------------------------------
    // Glob — find files matching a pattern
    // -----------------------------------------------------------------------

    private string ExecuteGlob(IReadOnlyDictionary<string, JsonElement> input)
    {
        var pattern  = Str(input, "pattern");
        var basePath = TryStr(input, "path") is { } p ? ResolvePath(p) : workingDirectory;

        // Handle **/<ext> → recursive search; plain <ext> → top-level only
        bool   recursive;
        string filePattern;

        if (pattern.Contains("**/"))
        {
            recursive   = true;
            filePattern = pattern[(pattern.LastIndexOf('/') + 1)..];
            if (string.IsNullOrEmpty(filePattern)) filePattern = "*";
        }
        else if (pattern.Contains('/'))
        {
            recursive = false;
            basePath  = ResolvePath(Path.GetDirectoryName(pattern)!);
            filePattern = Path.GetFileName(pattern);
        }
        else
        {
            recursive   = false;
            filePattern = pattern;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            var files = Directory.GetFiles(basePath, filePattern, searchOption)
                .Select(f => Path.GetRelativePath(workingDirectory, f))
                .OrderBy(f => f)
                .ToList();

            return files.Count == 0
                ? "No files matched."
                : string.Join("\n", files);
        }
        catch (Exception ex)
        {
            return $"Glob error: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------------
    // Grep — search for a regex pattern inside files
    // -----------------------------------------------------------------------

    private string ExecuteGrep(IReadOnlyDictionary<string, JsonElement> input)
    {
        var pattern  = Str(input, "pattern");
        var basePath = TryStr(input, "path") is { } p ? ResolvePath(p) : workingDirectory;
        var include  = TryStr(input, "include") ?? "*";

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(5)); }
        catch (ArgumentException ex) { return $"Invalid regex: {ex.Message}"; }

        var sb = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(basePath, include, SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!regex.IsMatch(lines[i])) continue;
                    var rel = Path.GetRelativePath(workingDirectory, file);
                    sb.AppendLine($"{rel}:{i + 1}: {lines[i].Trim()}");
                }
            }
            catch { /* skip binary/unreadable files */ }
        }

        return sb.Length == 0 ? "No matches found." : sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Bash — execute a shell command in the repo root (30 s timeout)
    // -----------------------------------------------------------------------

    private async Task<string> ExecuteBashAsync(
        IReadOnlyDictionary<string, JsonElement> input,
        CancellationToken ct)
    {
        var command = Str(input, "command");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(30));

        var psi = new ProcessStartInfo("/bin/sh")
        {
            ArgumentList           = { "-c", command },
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start shell process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = await process.StandardError.ReadToEndAsync(linked.Token);
        await process.WaitForExitAsync(linked.Token);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout)) sb.Append(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) sb.Append(stderr);
        if (process.ExitCode != 0)             sb.AppendLine($"\n[Exit code: {process.ExitCode}]");

        return sb.Length == 0 ? "(no output)" : sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Helper: resolve path relative to workingDirectory
    // -----------------------------------------------------------------------

    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(workingDirectory, path));

    private static string Str(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var el) ? el.GetString() ?? "" : "";

    private static string? TryStr(IReadOnlyDictionary<string, JsonElement> d, string key) =>
        d.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // -----------------------------------------------------------------------
    // Tool definitions (passed to the API as the Tools list)
    // -----------------------------------------------------------------------

    private static IReadOnlyList<ToolUnion> BuildDefinitions() =>
    [
        Def("Read",
            "Read the contents of a file. Line numbers are prepended to each line.",
            Obj(new Dictionary<string, object>
            {
                ["path"] = Prop("string", "Relative or absolute path of the file to read.")
            }, ["path"])),

        Def("Write",
            "Write content to a file, creating parent directories as needed. Overwrites existing files.",
            Obj(new Dictionary<string, object>
            {
                ["path"]    = Prop("string", "Path of the file to write."),
                ["content"] = Prop("string", "Full content to write to the file.")
            }, ["path", "content"])),

        Def("Edit",
            "Replace an exact string occurrence in a file. The old_string must be unique in the file.",
            Obj(new Dictionary<string, object>
            {
                ["path"]       = Prop("string", "Path of the file to edit."),
                ["old_string"] = Prop("string", "Exact string to replace (must appear exactly once)."),
                ["new_string"] = Prop("string", "Replacement string.")
            }, ["path", "old_string", "new_string"])),

        Def("MultiEdit",
            "Apply multiple string replacements to a single file in one operation.",
            new InputSchema
            {
                Type       = Se("object"),
                Properties = new Dictionary<string, JsonElement>
                {
                    ["path"]  = Se(new { type = "string", description = "Path of the file to edit." }),
                    ["edits"] = Se(new
                    {
                        type        = "array",
                        description = "List of edits to apply in order.",
                        items       = new
                        {
                            type       = "object",
                            properties = new
                            {
                                old_string = new { type = "string" },
                                new_string = new { type = "string" }
                            },
                            required = new[] { "old_string", "new_string" }
                        }
                    })
                },
                Required = ["path", "edits"]
            }),

        Def("Glob",
            "Find files matching a glob pattern (e.g. '**/*.cs', 'src/**/*.ts').",
            Obj(new Dictionary<string, object>
            {
                ["pattern"] = Prop("string", "Glob pattern. Use **/ prefix for recursive search."),
                ["path"]    = Prop("string", "Base directory to search (defaults to repo root).")
            }, ["pattern"])),

        Def("Grep",
            "Search for a regular expression pattern inside files.",
            Obj(new Dictionary<string, object>
            {
                ["pattern"] = Prop("string", "Regex pattern to search for."),
                ["path"]    = Prop("string", "Directory to search (defaults to repo root)."),
                ["include"] = Prop("string", "File name glob filter, e.g. '*.cs'. Defaults to all files.")
            }, ["pattern"])),

        Def("Bash",
            "Execute a shell command in the repository root. Timeout: 30 seconds.",
            Obj(new Dictionary<string, object>
            {
                ["command"] = Prop("string", "Shell command to run.")
            }, ["command"])),
    ];

    private static ToolUnion Def(string name, string description, InputSchema schema) =>
        (ToolUnion)new Tool { Name = name, Description = description, InputSchema = schema };

    private static InputSchema Obj(Dictionary<string, object> props, string[] required) =>
        new()
        {
            Type       = Se("object"),
            Properties = props.ToDictionary(kvp => kvp.Key, kvp => Se(kvp.Value)),
            Required   = required
        };

    private static object Prop(string type, string description) =>
        new { type, description };

    private static JsonElement Se<T>(T value) =>
        JsonSerializer.SerializeToElement(value);
}
