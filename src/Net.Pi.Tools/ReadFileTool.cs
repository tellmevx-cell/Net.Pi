using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class ReadFileTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "read_file";
    public string Description => "Reads a text file from disk. Can read lines with line numbers and optional offset/limit.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "The file path (relative to workspace or absolute)" },
            offset = new { type = "integer", description = "Line number to start reading from (1-based, optional)" },
            limit = new { type = "integer", description = "Maximum number of lines to read (optional)" }
        },
        required = new[] { "path" }
    };

    public ReadFileTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var path = node?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Error("Missing required parameter: 'path'.");
            }

            var (isAllowed, fullPath, errMsg) = PathGuard.ResolveAndValidate(_workspaceRoot, path);
            if (!isAllowed) return ToolResult.Error(errMsg!);

            if (!File.Exists(fullPath))
            {
                return ToolResult.Error($"File not found: {path}");
            }

            var offset = node?["offset"]?.GetValue<int>() ?? 1;
            var limit = node?["limit"]?.GetValue<int>() ?? 2000;

            if (offset < 1) offset = 1;

            var lines = await File.ReadAllLinesAsync(fullPath, ct).ConfigureAwait(false);
            var sb = new StringBuilder();

            var startIdx = offset - 1;
            var count = Math.Min(limit, Math.Max(0, lines.Length - startIdx));

            for (int i = 0; i < count; i++)
            {
                var lineNum = startIdx + i + 1;
                sb.AppendLine($"{lineNum,6}\t{lines[startIdx + i]}");
            }

            if (startIdx + count < lines.Length)
            {
                sb.AppendLine($"... ({lines.Length - (startIdx + count)} more lines remaining)");
            }

            return ToolResult.Ok(sb.ToString(), new { totalLines = lines.Length });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to read file: {ex.Message}");
        }
    }
}
