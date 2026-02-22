using System.Security;
using System.Text.Json;
using TaskFinisher.Tools;
using Xunit;

namespace TaskFinisher.Tests;

public sealed class FilesystemToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FilesystemTools _tools;

    public FilesystemToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tools = new FilesystemTools();
    }

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "hello.txt"), "Hello, World!");

        var result = await _tools.ExecuteAsync(
            "read_file",
            MakeInput(("path", "hello.txt")),
            _tempDir);

        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public async Task ReadFile_NonExistentFile_ReturnsError()
    {
        var result = await _tools.ExecuteAsync(
            "read_file",
            MakeInput(("path", "nonexistent.txt")),
            _tempDir);

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task WriteFile_CreatesFile()
    {
        var result = await _tools.ExecuteAsync(
            "write_file",
            MakeInput(("path", "output.txt"), ("content", "Test content")),
            _tempDir);

        Assert.Contains("Successfully wrote", result);
        Assert.Equal("Test content", File.ReadAllText(Path.Combine(_tempDir, "output.txt")));
    }

    [Fact]
    public async Task WriteFile_CreatesSubdirectories()
    {
        var result = await _tools.ExecuteAsync(
            "write_file",
            MakeInput(("path", "subdir/nested/file.txt"), ("content", "Nested")),
            _tempDir);

        Assert.Contains("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "subdir", "nested", "file.txt")));
    }

    [Fact]
    public async Task ListDirectory_ReturnsEntries()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "b.cs"), "");

        var result = await _tools.ExecuteAsync(
            "list_directory",
            MakeInput(("path", ".")),
            _tempDir);

        Assert.Contains("a.cs", result);
        Assert.Contains("b.cs", result);
    }

    [Fact]
    public async Task PathTraversal_IsBlocked()
    {
        var result = await _tools.ExecuteAsync(
            "read_file",
            MakeInput(("path", "../../etc/passwd")),
            _tempDir);

        Assert.StartsWith("Error: Security violation", result);
    }

    [Fact]
    public async Task PathTraversal_AbsolutePath_IsBlocked()
    {
        var result = await _tools.ExecuteAsync(
            "read_file",
            MakeInput(("path", "/etc/passwd")),
            _tempDir);

        Assert.StartsWith("Error: Security violation", result);
    }

    [Fact]
    public async Task SearchInFiles_FindsMatches()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "public class Foo { }");
        File.WriteAllText(Path.Combine(_tempDir, "other.cs"), "namespace Bar { }");

        var result = await _tools.ExecuteAsync(
            "search_in_files",
            MakeInput(("pattern", "class Foo"), ("directory", ".")),
            _tempDir);

        Assert.Contains("code.cs", result);
        Assert.Contains("class Foo", result);
    }

    [Fact]
    public async Task DeleteFile_RemovesFile()
    {
        var filePath = Path.Combine(_tempDir, "to-delete.txt");
        File.WriteAllText(filePath, "delete me");

        var result = await _tools.ExecuteAsync(
            "delete_file",
            MakeInput(("path", "to-delete.txt")),
            _tempDir);

        Assert.Contains("Deleted", result);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task UnknownTool_ReturnsError()
    {
        var result = await _tools.ExecuteAsync(
            "unknown_tool",
            MakeInput(("path", "file.txt")),
            _tempDir);

        Assert.StartsWith("Error: Unknown tool", result);
    }

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
