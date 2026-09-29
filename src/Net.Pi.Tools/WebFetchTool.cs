using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class WebFetchTool : ITool
{
    private readonly HttpClient _httpClient;

    public string Name => "web_fetch";
    public string Description => "Fetches content from a URL (HTML, JSON, plain text) and converts HTML to readable plain text / markdown.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            url = new { type = "string", description = "The HTTP or HTTPS URL to fetch" },
            prompt = new { type = "string", description = "Optional hint on what information you are seeking on this page" },
            max_chars = new { type = "integer", description = "Maximum characters to return (default 10000)" }
        },
        required = new[] { "url" }
    };

    public WebFetchTool(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Mozilla", "5.0")
            );
        }
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var url = node?["url"]?.GetValue<string>();
            var maxChars = node?["max_chars"]?.GetValue<int>() ?? 10000;

            if (string.IsNullOrWhiteSpace(url))
            {
                return ToolResult.Error("Missing required parameter: 'url'.");
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ToolResult.Error($"HTTP request to '{url}' failed with status {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            var rawBytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            var html = Encoding.UTF8.GetString(rawBytes);

            string resultText;
            if (mediaType.Contains("json") || url.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                resultText = html;
            }
            else
            {
                resultText = ConvertHtmlToText(html);
            }

            if (resultText.Length > maxChars)
            {
                resultText = resultText[..maxChars] + $"\n... [Truncated: {resultText.Length - maxChars} characters omitted]";
            }

            return ToolResult.Ok(resultText, new { url, length = resultText.Length });
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to fetch '{argumentsJson}': {ex.Message}");
        }
    }

    private static string ConvertHtmlToText(string html)
    {
        // 1. Remove comments
        var text = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
        // 2. Remove script and style tags
        text = Regex.Replace(text, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<svg\b[^<]*(?:(?!<\/svg>)<[^<]*)*<\/svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // 3. Convert headers and line breaks
        text = Regex.Replace(text, @"<(h[1-6]|p|div|tr|br|li)[^>]*>", "\n", RegexOptions.IgnoreCase);

        // 4. Strip remaining HTML tags
        text = Regex.Replace(text, @"<[^>]+>", " ", RegexOptions.Singleline);

        // 5. Decode common HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // 6. Clean redundant whitespaces
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l));

        return string.Join("\n", lines);
    }
}
