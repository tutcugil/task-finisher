using System.Text.Json;
using Anthropic.Models.Messages;

namespace TaskFinisher.Tools;

public static class ToolDefinitions
{
    public static IReadOnlyList<ToolUnion> All =>
    [
        ReadFileTool,
        WriteFileTool,
        ListDirectoryTool,
        SearchInFilesTool,
        DeleteFileTool,
    ];

    public static readonly Tool ReadFileTool = new()
    {
        Name = "read_file",
        Description = """
            Read the full contents of a file at the given relative path from the repository root.
            Returns the file content as a string, or an error message if the file does not exist.
            """,
        InputSchema = MakeSchema(
            required: ["path"],
            properties: new()
            {
                ["path"] = Prop("string", "Relative path to the file from the repository root, e.g. src/MyClass.cs")
            })
    };

    public static readonly Tool WriteFileTool = new()
    {
        Name = "write_file",
        Description = """
            Create or overwrite a file at the given relative path with the provided content.
            Creates parent directories automatically if they do not exist.
            """,
        InputSchema = MakeSchema(
            required: ["path", "content"],
            properties: new()
            {
                ["path"] = Prop("string", "Relative path to the file from the repository root"),
                ["content"] = Prop("string", "The full content to write to the file")
            })
    };

    public static readonly Tool ListDirectoryTool = new()
    {
        Name = "list_directory",
        Description = """
            List all files and subdirectories in the given directory.
            Use relative paths from the repository root. Use "." for the root.
            Returns a JSON array of entries with name, type (file or directory), and path fields.
            """,
        InputSchema = MakeSchema(
            required: ["path"],
            properties: new()
            {
                ["path"] = Prop("string", "Relative directory path from repository root. Use '.' for root.")
            })
    };

    public static readonly Tool SearchInFilesTool = new()
    {
        Name = "search_in_files",
        Description = """
            Search for a text pattern across all files in a directory (recursive).
            Returns matching lines with file path, line number, and line content.
            Limited to the first 200 matches.
            """,
        InputSchema = MakeSchema(
            required: ["pattern", "directory"],
            properties: new()
            {
                ["pattern"] = Prop("string", "Text or regex pattern to search for"),
                ["directory"] = Prop("string", "Relative directory to search in. Use '.' for the full repo."),
                ["file_glob"] = Prop("string", "Optional glob pattern to filter files, e.g. '*.cs' or '*.json'")
            })
    };

    public static readonly Tool DeleteFileTool = new()
    {
        Name = "delete_file",
        Description = """
            Delete a file at the given relative path.
            Use with caution.
            """,
        InputSchema = MakeSchema(
            required: ["path"],
            properties: new()
            {
                ["path"] = Prop("string", "Relative path to the file to delete")
            })
    };

    private static InputSchema MakeSchema(
        string[] required,
        Dictionary<string, JsonElement> properties) =>
        new()
        {
            Type = JsonSerializer.SerializeToElement("object"),
            Required = required,
            Properties = properties
        };

    private static JsonElement Prop(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });
}
