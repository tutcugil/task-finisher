using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TaskFinisher.Tools;
using Xunit;

namespace TaskFinisher.Tests;

/// <summary>
/// Tests for <see cref="AgentTools"/> — the tool executor used by the Claude agentic loop.
/// (File kept as FilesystemToolsTests.cs for backwards compatibility with CI tooling.)
/// </summary>
public sealed class AgentToolsTests : IDisposable
{
    private readonly string    _tempDir;
    private readonly AgentTools _tools;

    public AgentToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tools = new AgentTools(_tempDir, NullLogger.Instance);
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Read_ExistingFile_ReturnsNumberedContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "hello.txt"), "line one\nline two");

        var result = await _tools.ExecuteAsync(
            "Read", MakeInput(("path", "hello.txt")), CancellationToken.None);

        Assert.Contains("line one", result);
        Assert.Contains("line two", result);
        // Line numbers must be prepended
        Assert.Contains("1\t", result);
        Assert.Contains("2\t", result);
    }

    [Fact]
    public async Task Read_NonExistentFile_ReturnsNotFound()
    {
        var result = await _tools.ExecuteAsync(
            "Read", MakeInput(("path", "missing.txt")), CancellationToken.None);

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Write
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Write_CreatesFile()
    {
        var result = await _tools.ExecuteAsync(
            "Write",
            MakeInput(("path", "output.txt"), ("content", "Test content")),
            CancellationToken.None);

        Assert.Contains("Wrote", result);
        Assert.Equal("Test content", File.ReadAllText(Path.Combine(_tempDir, "output.txt")));
    }

    [Fact]
    public async Task Write_CreatesSubdirectories()
    {
        var result = await _tools.ExecuteAsync(
            "Write",
            MakeInput(("path", "subdir/nested/file.txt"), ("content", "Nested")),
            CancellationToken.None);

        Assert.Contains("Wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "subdir", "nested", "file.txt")));
    }

    // -----------------------------------------------------------------------
    // Edit
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Edit_ReplacesString()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.txt"), "Hello World");

        var result = await _tools.ExecuteAsync(
            "Edit",
            MakeInput(("path", "code.txt"), ("old_string", "World"), ("new_string", "Claude")),
            CancellationToken.None);

        Assert.Contains("applied", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Hello Claude", File.ReadAllText(Path.Combine(_tempDir, "code.txt")));
    }

    [Fact]
    public async Task Edit_OldStringNotFound_ReturnsError()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.txt"), "Hello World");

        var result = await _tools.ExecuteAsync(
            "Edit",
            MakeInput(("path", "code.txt"), ("old_string", "NotPresent"), ("new_string", "X")),
            CancellationToken.None);

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // MultiEdit
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultiEdit_AppliesMultipleEdits()
    {
        File.WriteAllText(Path.Combine(_tempDir, "multi.txt"), "foo bar baz");

        var edits = new[]
        {
            new { old_string = "foo", new_string = "FOO" },
            new { old_string = "bar", new_string = "BAR" }
        };

        var input = new Dictionary<string, JsonElement>
        {
            ["path"]  = JsonSerializer.SerializeToElement("multi.txt"),
            ["edits"] = JsonSerializer.SerializeToElement(edits)
        };

        var result = await _tools.ExecuteAsync("MultiEdit", input, CancellationToken.None);

        Assert.Contains("2", result); // "2 edit(s) applied"
        Assert.Equal("FOO BAR baz", File.ReadAllText(Path.Combine(_tempDir, "multi.txt")));
    }

    // -----------------------------------------------------------------------
    // Glob
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Glob_FindsMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.cs"),  "");
        File.WriteAllText(Path.Combine(_tempDir, "b.cs"),  "");
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), "");

        var result = await _tools.ExecuteAsync(
            "Glob", MakeInput(("pattern", "**/*.cs")), CancellationToken.None);

        Assert.Contains("a.cs", result);
        Assert.Contains("b.cs", result);
        Assert.DoesNotContain("c.txt", result);
    }

    [Fact]
    public async Task Glob_NoMatches_ReturnsMessage()
    {
        var result = await _tools.ExecuteAsync(
            "Glob", MakeInput(("pattern", "**/*.xyz")), CancellationToken.None);

        Assert.Contains("No files matched", result);
    }

    // -----------------------------------------------------------------------
    // Grep
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Grep_FindsMatchingLines()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"),
            "public class Foo { }\npublic class Bar { }");

        var result = await _tools.ExecuteAsync(
            "Grep", MakeInput(("pattern", "class Foo")), CancellationToken.None);

        Assert.Contains("code.cs", result);
        Assert.Contains("class Foo", result);
        Assert.DoesNotContain("class Bar", result);
    }

    [Fact]
    public async Task Grep_NoMatches_ReturnsMessage()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "hello world");

        var result = await _tools.ExecuteAsync(
            "Grep", MakeInput(("pattern", "nomatch_xyz")), CancellationToken.None);

        Assert.Contains("No matches", result);
    }

    // -----------------------------------------------------------------------
    // Bash
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bash_EchoCommand_ReturnsOutput()
    {
        var result = await _tools.ExecuteAsync(
            "Bash",
            MakeInput(("command", "echo hello_from_bash")),
            CancellationToken.None);

        Assert.Contains("hello_from_bash", result);
    }

    // -----------------------------------------------------------------------
    // Unknown tool
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnknownTool_ReturnsUnknownMessage()
    {
        var result = await _tools.ExecuteAsync(
            "unknown_tool", MakeInput(("path", "file.txt")), CancellationToken.None);

        Assert.Contains("Unknown tool", result);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyDictionary<string, JsonElement> MakeInput(
        params (string key, string value)[] pairs) =>
        pairs.ToDictionary(
            p => p.key,
            p => JsonSerializer.SerializeToElement(p.value));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
