using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Net.Pi.Ai.Models;

namespace Net.Pi.Ai;

public class OpenAiCompatibleClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public string DefaultModel => _defaultModel;

    public OpenAiCompatibleClient(string apiKey, string baseUrl = "https://api.openai.com/v1", string defaultModel = "gpt-4o-mini", HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _defaultModel = defaultModel;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = options?.Model ?? _defaultModel;
        var requestPayload = BuildRequestBody(messages, tools, options, model, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
        request.Content = new StringContent(requestPayload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"API request failed with code {response.StatusCode}: {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:")) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield return new ChatStreamChunk(FinishReason: "stop");
                break;
            }

            JsonNode? rootNode;
            try
            {
                rootNode = JsonNode.Parse(data);
            }
            catch
            {
                continue;
            }

            if (rootNode?["choices"] is JsonArray choices && choices.Count > 0)
            {
                var choice = choices[0];
                var delta = choice?["delta"];
                var finishReason = choice?["finish_reason"]?.GetValue<string?>();
                string? text = delta?["content"]?.GetValue<string?>();
                string? reasoning = delta?["reasoning_content"]?.GetValue<string?>();

                List<ToolCallDelta>? toolDeltas = null;
                if (delta?["tool_calls"] is JsonArray toolCallsJson)
                {
                    toolDeltas = new List<ToolCallDelta>();
                    foreach (var tc in toolCallsJson)
                    {
                        var index = tc?["index"]?.GetValue<int>() ?? 0;
                        var id = tc?["id"]?.GetValue<string?>();
                        var fn = tc?["function"];
                        var fnName = fn?["name"]?.GetValue<string?>();
                        var fnArgs = fn?["arguments"]?.GetValue<string?>();
                        toolDeltas.Add(new ToolCallDelta(index, id, fnName, fnArgs));
                    }
                }

                if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(reasoning) || toolDeltas != null || finishReason != null)
                {
                    yield return new ChatStreamChunk(text, reasoning, toolDeltas, finishReason);
                }
            }
        }
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default)
    {
        var textBuilder = new StringBuilder();
        var toolCallsMap = new Dictionary<int, (string? id, string? name, StringBuilder args)>();
        string? finishReason = null;

        await foreach (var chunk in StreamChatAsync(messages, tools, options, ct).ConfigureAwait(false))
        {
            if (chunk.DeltaText != null)
            {
                textBuilder.Append(chunk.DeltaText);
            }
            if (chunk.FinishReason != null)
            {
                finishReason = chunk.FinishReason;
            }
            if (chunk.ToolDeltas != null)
            {
                foreach (var d in chunk.ToolDeltas)
                {
                    if (!toolCallsMap.TryGetValue(d.Index, out var current))
                    {
                        current = (d.Id, d.Name, new StringBuilder());
                        toolCallsMap[d.Index] = current;
                    }
                    if (!string.IsNullOrEmpty(d.Id)) current.id = d.Id;
                    if (!string.IsNullOrEmpty(d.Name)) current.name = d.Name;
                    if (!string.IsNullOrEmpty(d.ArgumentsDelta)) current.args.Append(d.ArgumentsDelta);
                    toolCallsMap[d.Index] = current;
                }
            }
        }

        var completedTools = toolCallsMap.Values
            .Where(t => !string.IsNullOrEmpty(t.name))
            .Select(t => new ToolCall(t.id ?? Guid.NewGuid().ToString("n"), t.name!, t.args.ToString()))
            .ToList();

        return new ChatCompletionResult(textBuilder.ToString(), completedTools, finishReason);
    }

    private static JsonObject BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        ChatCompletionOptions? options,
        string model,
        bool stream)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = stream
        };

        if (options?.Temperature.HasValue == true)
        {
            body["temperature"] = options.Temperature.Value;
        }

        if (options?.MaxTokens.HasValue == true)
        {
            body["max_tokens"] = options.MaxTokens.Value;
        }

        var msgsArray = new JsonArray();
        foreach (var msg in messages)
        {
            var msgObj = new JsonObject
            {
                ["role"] = msg.Role
            };

            if (msg.Content != null)
            {
                msgObj["content"] = msg.Content;
            }

            if (msg.ToolCallId != null)
            {
                msgObj["tool_call_id"] = msg.ToolCallId;
            }

            if (msg.Name != null)
            {
                msgObj["name"] = msg.Name;
            }

            if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                var tcArray = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    tcArray.Add(new JsonObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = tc.Arguments
                        }
                    });
                }
                msgObj["tool_calls"] = tcArray;
            }

            msgsArray.Add(msgObj);
        }
        body["messages"] = msgsArray;

        if (tools != null && tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var t in tools)
            {
                var toolObj = new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = JsonSerializer.SerializeToNode(t.ParametersSchema)
                    }
                };
                toolsArray.Add(toolObj);
            }
            body["tools"] = toolsArray;
        }

        return body;
    }
}
