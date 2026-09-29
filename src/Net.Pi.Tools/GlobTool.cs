using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class GlobTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "glob";
    public string Description => "Find files matching a glob pattern (e.g., '**/*.cs', 'src/**/*.json'). Automatically skips build and ignore directories (.git, bin, obj, node_modules).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The glob pattern to match file paths against, e.g. '**/*.cs'" },
            path = new { type = "string", description = "Base directory to search in (optional, defaults to workspace root)" }
        },
        required = new[] { "pattern" }
    };

    private static readonly string[] DefaultExcludes =
    {
        "**/.git/**",
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/.vs/**",
        "**/dist/**",
        "**/coverage/**"
    };

    public GlobTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var pattern = node?["pattern"]?.GetValue<string>();
            var path = node?["path"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(pattern))
            {
                return Task.FromResult(ToolResult.Error("Missing required parameter: 'pattern'."));
            }

            var (isAllowed, baseDir, errMsg) = string.IsNullOrWhiteSpace(path)
                ? (true, _workspaceRoot, null)
                : PathGuard.ResolveAndValidate(_workspaceRoot, path);

            if (!isAllowed) return Task.FromResult(ToolResult.Error(errMsg!));

            if (!Directory.Exists(baseDir))
            {
                return Task.FromResult(ToolResult.Error($"Directory not found: {baseDir}"));
            }

            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            foreach (var exc in DefaultExcludes)
            {
                matcher.AddExclude(exc);
            }

            var dirInfo = new DirectoryInfo(baseDir);
            var result = matcher.Execute(new DirectoryInfoWrapper(dirInfo));

            var files = result.Files
                .Select(f => f.Path.Replace('\\', '/'))
                .OrderBy(p => p)
                .ToList();

            if (files.Count == 0)
            {
                return Task.FromResult(ToolResult.Ok($"No files matched pattern '{pattern}' in '{baseDir}'."));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Matched {files.Count} file(s) for pattern '{pattern}':");
            sb.AppendLine();
            foreach (var f in files.Take(200))
            {
                sb.AppendLine($"  {f}");
            }
            if (files.Count > 200)
            {
                sb.AppendLine($"  ... and {files.Count - 200} more files.");
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString(), new { count = files.Count, files }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Glob execution failed: {ex.Message}"));
        }
    }
}
