using System.Text.Json.Nodes;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class WriteFileTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "write_file";
    public string Description => "Writes content to a file. Overwrites existing file and creates parent directories if needed.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "The file path (relative to workspace or absolute)" },
            content = new { type = "string", description = "The full text content to write" }
        },
        required = new[] { "path", "content" }
    };

    public WriteFileTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var path = node?["path"]?.GetValue<string>();
            var content = node?["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Error("Missing required parameter: 'path'.");
            }
            if (content == null)
            {
                return ToolResult.Error("Missing required parameter: 'content'.");
            }

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(fullPath, content, ct).ConfigureAwait(false);
            return ToolResult.Ok($"Successfully wrote {content.Length} characters to '{path}'.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to write file: {ex.Message}");
        }
    }
}
