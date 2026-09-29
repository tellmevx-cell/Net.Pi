using System.Net;
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
    private readonly int _maxRetries = 3;
    private readonly TimeSpan _readInactivityTimeout = TimeSpan.FromSeconds(60);

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
        var jsonContent = requestPayload.ToJsonString();

        HttpResponseMessage? response = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                var statusCode = (int)response.StatusCode;
                var isTransient = statusCode == 429 || (statusCode >= 500 && statusCode < 600);
                if (isTransient && attempt < _maxRetries)
                {
                    response.Dispose();
                    response = null;
                    var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt) + Random.Shared.Next(100, 300));
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"API request failed with code {response.StatusCode}: {err}", null, response.StatusCode);
            }
            catch (HttpRequestException) when (attempt < _maxRetries)
            {
                response?.Dispose();
                response = null;
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt) + Random.Shared.Next(100, 300));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Failed to establish stream connection with LLM provider after retries.");
        }

        using (response)
        using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var dataBuffer = new StringBuilder();
            string? lastObservedFinishReason = null;

            while (!ct.IsCancellationRequested)
            {
                string? line;
                using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readCts.CancelAfter(_readInactivityTimeout);
                    try
                    {
                        line = await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Stream read timed out after {_readInactivityTimeout.TotalSeconds} seconds of inactivity.");
                    }
                }

                if (line == null) // End of stream
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    // Empty line dispatches the accumulated SSE event
                    if (dataBuffer.Length > 0)
                    {
                        var data = dataBuffer.ToString().Trim();
                        dataBuffer.Clear();

                        if (data == "[DONE]")
                        {
                            // Do not unconditionally overwrite finish_reason if one was already emitted
                            if (lastObservedFinishReason == null)
                            {
                                yield return new ChatStreamChunk(FinishReason: "stop");
                            }
                            break;
                        }

                        var chunk = ParseSseDataPayload(data, ref lastObservedFinishReason);
                        if (chunk != null)
                        {
                            yield return chunk;
                        }
                    }
                    continue;
                }

                if (line.StartsWith(':')) // SSE comment (ping / keep-alive)
                {
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var content = line["data:".Length..].TrimStart();
                    dataBuffer.AppendLine(content);
                }
            }

            // Flush any remaining data at end of stream
            if (dataBuffer.Length > 0)
            {
                var data = dataBuffer.ToString().Trim();
                if (data != "[DONE]")
                {
                    var chunk = ParseSseDataPayload(data, ref lastObservedFinishReason);
                    if (chunk != null)
                    {
                        yield return chunk;
                    }
                }
            }
        }
    }

    private static ChatStreamChunk? ParseSseDataPayload(string data, ref string? lastObservedFinishReason)
    {
        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            // Invalid JSON chunk in stream
            return null;
        }

        UsageStats? usage = null;
        if (rootNode?["usage"] is JsonObject usageObj)
        {
            var promptTokens = usageObj["prompt_tokens"]?.GetValue<int>() ?? 0;
            var completionTokens = usageObj["completion_tokens"]?.GetValue<int>() ?? 0;
            var totalTokens = usageObj["total_tokens"]?.GetValue<int>() ?? 0;
            var reasoningTokens = usageObj["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0;
            usage = new UsageStats(promptTokens, completionTokens, reasoningTokens, totalTokens);
        }

        if (rootNode?["choices"] is JsonArray choices && choices.Count > 0)
        {
            var choice = choices[0];
            var delta = choice?["delta"];
            var finishReason = choice?["finish_reason"]?.GetValue<string?>();
            if (!string.IsNullOrEmpty(finishReason))
            {
                lastObservedFinishReason = finishReason;
            }

            string? text = delta?["content"]?.GetValue<string?>();
            string? reasoning = delta?["reasoning_content"]?.GetValue<string?>();

            List<ToolCallDelta>? toolDeltas = null;
            if (delta?["tool_calls"] is JsonArray toolCallsJson)
            {
                toolDeltas = new List<ToolCallDelta>();
                foreach (var tc in toolCallsJson)
                {
                    int index = 0;
                    if (tc?["index"] is JsonValue idxVal)
                    {
                        if (idxVal.TryGetValue<int>(out var parsedInt))
                        {
                            index = parsedInt;
                        }
                        else if (int.TryParse(idxVal.ToString(), out var parsedStr))
                        {
                            index = parsedStr;
                        }
                    }

                    var id = tc?["id"]?.GetValue<string?>();
                    var fn = tc?["function"];
                    var fnName = fn?["name"]?.GetValue<string?>();
                    var fnArgs = fn?["arguments"]?.GetValue<string?>();
                    toolDeltas.Add(new ToolCallDelta(index, id, fnName, fnArgs));
                }
            }

            if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(reasoning) || toolDeltas != null || finishReason != null || usage != null)
            {
                return new ChatStreamChunk(text, reasoning, toolDeltas, finishReason, usage);
            }
        }
        else if (usage != null)
        {
            return new ChatStreamChunk(Usage: usage);
        }

        return null;
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
