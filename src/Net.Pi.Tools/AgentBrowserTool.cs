using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class AgentBrowserTool : ITool
{
    private readonly string _edgePath;
    private readonly string _userDataDir;

    public string Name => "agent_browser";
    public string Description => "Browser automation tool for agents. Supports 'navigate' (loads page, executes JS and returns text snapshot), and 'screenshot' (captures visual image).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            action = new { type = "string", @enum = new[] { "navigate", "screenshot" }, description = "Action to perform: 'navigate' or 'screenshot'" },
            url = new { type = "string", description = "Target web page URL" },
            screenshot_path = new { type = "string", description = "Where to save the screenshot image (required if action='screenshot')" },
            timeout_seconds = new { type = "integer", description = "Timeout in seconds (default 20)" }
        },
        required = new[] { "action", "url" }
    };

    public AgentBrowserTool()
    {
        _edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        _userDataDir = Path.Combine(Path.GetTempPath(), "net_pi_browser_profile");
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var action = node?["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "navigate";
            var url = node?["url"]?.GetValue<string>();
            var timeoutSeconds = node?["timeout_seconds"]?.GetValue<int>() ?? 20;

            if (string.IsNullOrWhiteSpace(url))
            {
                return ToolResult.Error("Missing required parameter: 'url'.");
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (action == "screenshot")
            {
                var outputPath = node?["screenshot_path"]?.GetValue<string>() ?? 
                                 Path.Combine(Directory.GetCurrentDirectory(), $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                var success = await CaptureScreenshotAsync(url, outputPath, timeoutSeconds, ct).ConfigureAwait(false);
                if (success && File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    return ToolResult.Ok($"Screenshot captured successfully at '{outputPath}' ({fileInfo.Length:N0} bytes).", new { path = outputPath });
                }
                return ToolResult.Error($"Failed to capture screenshot for '{url}'.");
            }
            else // navigate
            {
                var content = await NavigateAndExtractTextAsync(url, timeoutSeconds, ct).ConfigureAwait(false);
                return ToolResult.Ok(content, new { url });
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"agent_browser action failed: {ex.Message}");
        }
    }

    private async Task<string> NavigateAndExtractTextAsync(string url, int timeoutSeconds, CancellationToken ct)
    {
        // Try headless edge dump-dom first
        if (File.Exists(_edgePath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _edgePath,
                    Arguments = $"--headless=new --disable-gpu --virtual-time-budget=4000 --dump-dom \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = new Process { StartInfo = psi };
                var stdout = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                var rawHtml = stdout.ToString();
                if (!string.IsNullOrWhiteSpace(rawHtml) && rawHtml.Contains("<"))
                {
                    return FormatDomSnapshot(url, rawHtml);
                }
            }
            catch
            {
                // Fallback to WebFetch HTTP client
            }
        }

        // Fallback: fast HTTP Fetch and conversion
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0");
        var html = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        return FormatDomSnapshot(url, html);
    }

    private async Task<bool> CaptureScreenshotAsync(string url, string outputPath, int timeoutSeconds, CancellationToken ct)
    {
        if (!File.Exists(_edgePath)) return false;

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _edgePath,
            Arguments = $"--headless=new --disable-gpu --virtual-time-budget=3000 --screenshot=\"{outputPath}\" \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        return File.Exists(outputPath);
    }

    private static string FormatDomSnapshot(string url, string html)
    {
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "Untitled Page";

        // Clean styles/scripts
        var text = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Extract interactive buttons/links
        var links = new List<string>();
        foreach (Match m in Regex.Matches(text, @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var lText = Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "").Trim();
            if (!string.IsNullOrEmpty(lText) && lText.Length < 60)
            {
                links.Add($"[link \"{lText}\" -> {m.Groups[1].Value}]");
            }
            if (links.Count >= 15) break;
        }

        // Clean tags
        text = Regex.Replace(text, @"<(h[1-6]|p|div|tr|br|li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ", RegexOptions.Singleline);
        text = System.Net.WebUtility.HtmlDecode(text);

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l));
        var cleanBody = string.Join("\n", lines);

        if (cleanBody.Length > 8000)
        {
            cleanBody = cleanBody[..8000] + "\n... [Remaining content truncated]";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[Browser Snapshot]");
        sb.AppendLine($"Title: {title}");
        sb.AppendLine($"URL:   {url}");
        sb.AppendLine();
        if (links.Count > 0)
        {
            sb.AppendLine("Interactive Links:");
            foreach (var link in links) sb.AppendLine($"  {link}");
            sb.AppendLine();
        }
        sb.AppendLine("Page Content:");
        sb.AppendLine(cleanBody);

        return sb.ToString();
    }
}
