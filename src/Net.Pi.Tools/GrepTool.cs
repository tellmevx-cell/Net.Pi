using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class GrepTool : ITool
{
    private readonly string _workspaceRoot;

    public string Name => "grep";
    public string Description => "Search file contents using regular expressions. Skips binary files and build/cache directories (.git, bin, obj, node_modules).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { type = "string", description = "The regular expression or text to search for" },
            path = new { type = "string", description = "Directory or file to search in (optional, defaults to workspace root)" },
            glob = new { type = "string", description = "Filter files by glob pattern, e.g. '**/*.cs' or '*.json'" },
            case_sensitive = new { type = "boolean", description = "Whether search should be case-sensitive (default false)" },
            output_mode = new { type = "string", @enum = new[] { "content", "files_with_matches" }, description = "Output mode: 'content' (default) shows matching lines; 'files_with_matches' lists file paths only" },
            max_results = new { type = "integer", description = "Maximum number of results to return (default 100)" }
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

    public GrepTool(string? workspaceRoot = null)
    {
        _workspaceRoot = workspaceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var pattern = node?["pattern"]?.GetValue<string>();
            var path = node?["path"]?.GetValue<string>();
            var globPattern = node?["glob"]?.GetValue<string>() ?? "**/*";
            var caseSensitive = node?["case_sensitive"]?.GetValue<bool>() ?? false;
            var outputMode = node?["output_mode"]?.GetValue<string>() ?? "content";
            var maxResults = node?["max_results"]?.GetValue<int>() ?? 100;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                return ToolResult.Error("Missing required parameter: 'pattern'.");
            }

            var baseDir = string.IsNullOrWhiteSpace(path)
                ? _workspaceRoot
                : (Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path));

            // If path points directly to a single file
            if (File.Exists(baseDir))
            {
                return await SearchSingleFileAsync(baseDir, pattern, caseSensitive, outputMode, maxResults, ct).ConfigureAwait(false);
            }

            if (!Directory.Exists(baseDir))
            {
                return ToolResult.Error($"Directory not found: {baseDir}");
            }

            var matcher = new Matcher();
            matcher.AddInclude(globPattern);
            foreach (var exc in DefaultExcludes)
            {
                matcher.AddExclude(exc);
            }

            var dirInfo = new DirectoryInfo(baseDir);
            var matchedFiles = matcher.Execute(new DirectoryInfoWrapper(dirInfo)).Files;

            var regexOptions = RegexOptions.Compiled;
            if (!caseSensitive) regexOptions |= RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, regexOptions);

            var sb = new StringBuilder();
            int totalMatches = 0;
            var filesWithMatches = new List<string>();

            foreach (var fileResult in matchedFiles)
            {
                if (ct.IsCancellationRequested || totalMatches >= maxResults) break;

                var fullFilePath = Path.Combine(baseDir, fileResult.Path);
                if (IsBinaryFile(fullFilePath)) continue;

                var lines = await File.ReadAllLinesAsync(fullFilePath, ct).ConfigureAwait(false);
                bool fileHasMatch = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        fileHasMatch = true;
                        totalMatches++;

                        if (outputMode == "content")
                        {
                            var relPath = fileResult.Path.Replace('\\', '/');
                            sb.AppendLine($"{relPath}:{i + 1}: {lines[i]}");
                        }

                        if (totalMatches >= maxResults) break;
                    }
                }

                if (fileHasMatch)
                {
                    var relPath = fileResult.Path.Replace('\\', '/');
                    filesWithMatches.Add(relPath);
                    if (outputMode == "files_with_matches")
                    {
                        sb.AppendLine(relPath);
                    }
                }
            }

            if (totalMatches == 0)
            {
                return ToolResult.Ok($"No matches found for pattern '{pattern}'.");
            }

            var header = outputMode == "files_with_matches"
                ? $"Found matches in {filesWithMatches.Count} file(s):"
                : $"Found {totalMatches} match(es) for pattern '{pattern}':";

            return ToolResult.Ok($"{header}\n\n{sb.ToString().TrimEnd()}", new { totalMatches, filesCount = filesWithMatches.Count });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Grep failed: {ex.Message}");
        }
    }

    private static async Task<ToolResult> SearchSingleFileAsync(
        string filePath,
        string pattern,
        bool caseSensitive,
        string outputMode,
        int maxResults,
        CancellationToken ct)
    {
        var regexOptions = RegexOptions.Compiled;
        if (!caseSensitive) regexOptions |= RegexOptions.IgnoreCase;
        var regex = new Regex(pattern, regexOptions);

        var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        int matches = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
            {
                matches++;
                sb.AppendLine($"{Path.GetFileName(filePath)}:{i + 1}: {lines[i]}");
                if (matches >= maxResults) break;
            }
        }

        if (matches == 0) return ToolResult.Ok($"No matches found in '{filePath}'.");

        if (outputMode == "files_with_matches")
        {
            return ToolResult.Ok(filePath);
        }

        return ToolResult.Ok($"Found {matches} match(es) in '{filePath}':\n\n{sb.ToString().TrimEnd()}");
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            var buffer = new byte[512];
            using var stream = File.OpenRead(filePath);
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0) return true; // Null byte indicates binary
            }
            return false;
        }
        catch
        {
            return true;
        }
    }
}
