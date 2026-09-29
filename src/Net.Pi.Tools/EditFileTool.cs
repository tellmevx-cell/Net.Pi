using System.Text.Json.Nodes;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class EditFileTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "edit_file";
    public string Description => "Performs exact string replacement in a file. If old_string is not unique, fails unless replace_all is true.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "The file path (relative to workspace or absolute)" },
            old_string = new { type = "string", description = "The exact string to find and replace" },
            new_string = new { type = "string", description = "The replacement string" },
            replace_all = new { type = "boolean", description = "Whether to replace every occurrence" }
        },
        required = new[] { "path", "old_string", "new_string" }
    };

    public EditFileTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var path = node?["path"]?.GetValue<string>();
            var oldString = node?["old_string"]?.GetValue<string>();
            var newString = node?["new_string"]?.GetValue<string>();
            var replaceAll = node?["replace_all"]?.GetValue<bool>() ?? false;

            if (string.IsNullOrWhiteSpace(path)) return ToolResult.Error("Missing 'path'.");
            if (oldString == null) return ToolResult.Error("Missing 'old_string'.");
            if (newString == null) return ToolResult.Error("Missing 'new_string'.");

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path);
            if (!File.Exists(fullPath)) return ToolResult.Error($"File not found: {path}");

            var content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            if (!content.Contains(oldString))
            {
                return ToolResult.Error($"old_string not found in '{path}'. Make sure whitespace and formatting match exactly.");
            }

            int count = 0;
            int idx = 0;
            while ((idx = content.IndexOf(oldString, idx, StringComparison.Ordinal)) != -1)
            {
                count++;
                idx += oldString.Length;
            }

            if (!replaceAll && count > 1)
            {
                return ToolResult.Error($"old_string appears {count} times in '{path}'. Add more context or set replace_all=true.");
            }

            string updatedContent;
            if (replaceAll)
            {
                updatedContent = content.Replace(oldString, newString, StringComparison.Ordinal);
            }
            else
            {
                var firstIdx = content.IndexOf(oldString, StringComparison.Ordinal);
                updatedContent = content.Substring(0, firstIdx) + newString + content.Substring(firstIdx + oldString.Length);
            }

            await File.WriteAllTextAsync(fullPath, updatedContent, ct).ConfigureAwait(false);
            return ToolResult.Ok($"Successfully replaced {count} occurrence(s) in '{path}'.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to edit file: {ex.Message}");
        }
    }
}
