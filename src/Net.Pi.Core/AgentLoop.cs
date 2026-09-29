using System.Runtime.CompilerServices;
using System.Text;
using Net.Pi.Ai;
using Net.Pi.Ai.Models;
using Net.Pi.Core.Events;

namespace Net.Pi.Core;

public class AgentLoopOptions
{
    public int MaxTurns { get; set; } = 30;
    public string? SystemPrompt { get; set; }
    public ChatCompletionOptions? ChatOptions { get; set; }
}

public class AgentLoop
{
    private readonly ILlmClient _llmClient;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChatMessage> _history = new();
    private readonly AgentLoopOptions _options;

    public IReadOnlyList<ChatMessage> History => _history;
    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    public AgentLoop(ILlmClient llmClient, IEnumerable<ITool>? tools = null, AgentLoopOptions? options = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _options = options ?? new AgentLoopOptions();

        if (tools != null)
        {
            foreach (var t in tools)
            {
                _tools[t.Name] = t;
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            _history.Add(ChatMessage.System(_options.SystemPrompt));
        }
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public void AddMessage(ChatMessage message)
    {
        _history.Add(message);
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _history.Add(ChatMessage.User(userPrompt));

        var toolDefs = _tools.Values.Select(t => new ToolDefinition(
            t.Name,
            t.Description,
            t.ParametersSchema
        )).ToList();

        int turn = 0;
        while (turn < _options.MaxTurns && !ct.IsCancellationRequested)
        {
            turn++;
            yield return new AgentTurnStarted(turn);

            var textBuilder = new StringBuilder();
            var toolCallsMap = new Dictionary<int, (string? id, string? name, StringBuilder args)>();

            IAsyncEnumerable<ChatStreamChunk>? stream = null;
            Exception? streamError = null;
            try
            {
                stream = _llmClient.StreamChatAsync(_history, toolDefs, _options.ChatOptions, ct);
            }
            catch (Exception ex)
            {
                streamError = ex;
            }

            if (streamError != null)
            {
                yield return new AgentErrorOccurred(streamError, $"LLM stream error: {streamError.Message}");
                yield break;
            }

            await foreach (var chunk in stream!.WithCancellation(ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.ReasoningDelta))
                {
                    yield return new AgentReasoningDelta(chunk.ReasoningDelta);
                }

                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    textBuilder.Append(chunk.DeltaText);
                    yield return new AgentTextDelta(chunk.DeltaText);
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

            var completedToolCalls = toolCallsMap.Values
                .Where(t => !string.IsNullOrEmpty(t.name))
                .Select(t => new ToolCall(
                    string.IsNullOrEmpty(t.id) ? Guid.NewGuid().ToString("n") : t.id,
                    t.name!,
                    t.args.ToString()
                )).ToList();

            var assistantContent = textBuilder.ToString();
            var assistantMsg = ChatMessage.Assistant(
                assistantContent,
                completedToolCalls.Count > 0 ? completedToolCalls : null
            );
            _history.Add(assistantMsg);

            yield return new AgentTurnCompleted(turn, assistantContent, completedToolCalls.Count > 0);

            // If no tools were called, this task is complete
            if (completedToolCalls.Count == 0)
            {
                yield return new AgentRunCompleted(turn);
                yield break;
            }

            // Execute tools sequentially or concurrently (here deterministic sequential per tool call)
            foreach (var call in completedToolCalls)
            {
                yield return new ToolCallStarting(call.Id, call.Name, call.Arguments);

                ToolResult result;
                if (_tools.TryGetValue(call.Name, out var toolInstance))
                {
                    try
                    {
                        result = await toolInstance.ExecuteAsync(call.Arguments, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = ToolResult.Error($"Error executing tool {call.Name}: {ex.Message}");
                    }
                }
                else
                {
                    result = ToolResult.Error($"Unknown tool '{call.Name}'.");
                }

                _history.Add(ChatMessage.ToolResult(call.Id, result.Content, call.Name));
                yield return new ToolCallCompleted(call.Id, call.Name, result);
            }
        }

        yield return new AgentRunCompleted(turn);
    }
}
