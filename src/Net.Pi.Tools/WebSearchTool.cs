using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Net.Pi.Core;

namespace Net.Pi.Tools;

public class WebSearchResultItem
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
}

public class WebSearchTool : ITool
{
    private readonly HttpClient _httpClient;

    public string Name => "web_search";
    public string Description => "Searches the web for relevant pages, returning titles, URLs, and snippets.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "The search query keywords" },
            count = new { type = "integer", description = "Number of results to return (default 5, max 10)" }
        },
        required = new[] { "query" }
    };

    public WebSearchTool(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
        }
    }

    public async Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var query = node?["query"]?.GetValue<string>();
            var count = Math.Clamp(node?["count"]?.GetValue<int>() ?? 5, 1, 10);

            if (string.IsNullOrWhiteSpace(query))
            {
                return ToolResult.Error("Missing required parameter: 'query'.");
            }

            var results = await PerformBingSearchAsync(query, count, ct).ConfigureAwait(false);

            if (results.Count == 0)
            {
                return ToolResult.Ok($"No web search results found for query: '{query}'.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for: \"{query}\"\n");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.AppendLine($"{i + 1}. **{r.Title}**");
                sb.AppendLine($"   URL: {r.Url}");
                if (!string.IsNullOrWhiteSpace(r.Snippet))
                {
                    sb.AppendLine($"   Snippet: {r.Snippet}");
                }
                sb.AppendLine();
            }

            return ToolResult.Ok(sb.ToString().TrimEnd(), results);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Web search failed: {ex.Message}");
        }
    }

    private async Task<List<WebSearchResultItem>> PerformBingSearchAsync(string query, int count, CancellationToken ct)
    {
        var url = $"https://cn.bing.com/search?q={Uri.EscapeDataString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        using var res = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return new List<WebSearchResultItem>();
        }

        var html = await res.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        var items = new List<WebSearchResultItem>();

        // Regex pattern to extract Bing result blocks: <li class="b_algo"><h2><a href="...">title</a></h2>...
        var algoMatches = Regex.Matches(html, @"<li class=""b_algo""[^>]*>(.*?)</li>", RegexOptions.Singleline);
        foreach (Match m in algoMatches)
        {
            if (items.Count >= count) break;
            var block = m.Groups[1].Value;

            var titleMatch = Regex.Match(block, @"<h2[^>]*><a[^>]+href=""([^""]+)""[^>]*>(.*?)</a></h2>", RegexOptions.Singleline);
            if (!titleMatch.Success) continue;

            var href = titleMatch.Groups[1].Value;
            var title = StripHtml(titleMatch.Groups[2].Value);

            // Extract snippet from <p> inside the block
            var snippetMatch = Regex.Match(block, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline);
            var snippet = snippetMatch.Success ? StripHtml(snippetMatch.Groups[1].Value) : "";

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(href))
            {
                items.Add(new WebSearchResultItem
                {
                    Title = title,
                    Url = href,
                    Snippet = snippet
                });
            }
        }

        return items;
    }

    private static string StripHtml(string html)
    {
        var text = Regex.Replace(html, @"<[^>]+>", " ", RegexOptions.Singleline);
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
