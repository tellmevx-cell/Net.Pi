using System.Text;
using System.Text.Json.Nodes;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class ListDirTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "list_dir";
    public string Description => "Lists entries in a directory, showing whether each entry is a file or a folder.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Directory path to list (defaults to current directory if omitted)" }
        }
    };

    public ListDirTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            string? path = null;
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                var node = JsonNode.Parse(argumentsJson);
                path = node?["path"]?.GetValue<string>();
            }

            var targetPath = string.IsNullOrWhiteSpace(path)
                ? _workspaceRoot
                : (Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path));

            if (!Directory.Exists(targetPath))
            {
                return Task.FromResult(ToolResult.Error($"Directory not found: {targetPath}"));
            }

            var di = new DirectoryInfo(targetPath);
            var sb = new StringBuilder();
            sb.AppendLine($"Directory listing of: {di.FullName}");
            sb.AppendLine();

            foreach (var dir in di.GetDirectories())
            {
                sb.AppendLine($"  [DIR]  {dir.Name}");
            }

            foreach (var file in di.GetFiles())
            {
                sb.AppendLine($"  [FILE] {file.Name} ({file.Length:N0} bytes)");
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to list directory: {ex.Message}"));
        }
    }
}
