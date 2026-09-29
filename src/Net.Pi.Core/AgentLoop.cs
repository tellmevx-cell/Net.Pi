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
    public ContextCompactorOptions? CompactorOptions { get; set; }
}

public class AgentLoop
{
    private readonly ILlmClient _llmClient;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChatMessage> _history = new();
    private readonly AgentLoopOptions _options;
    private readonly ContextCompactor _compactor;

    public IReadOnlyList<ChatMessage> History => _history;
    public IReadOnlyDictionary<string, ITool> Tools => _tools;

    public AgentLoop(ILlmClient llmClient, IEnumerable<ITool>? tools = null, AgentLoopOptions? options = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _options = options ?? new AgentLoopOptions();
        _compactor = new ContextCompactor(_options.CompactorOptions);

        if (tools != null)
        {
            foreach (var t in tools)
            {
                _tools[t.Name] = t;
            }
        }

        ResetHistory();
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public void AddMessage(ChatMessage message)
    {
        _history.Add(message);
    }

    public void ResetHistory()
    {
        _history.Clear();
        if (!string.IsNullOrWhiteSpace(_options.SystemPrompt))
        {
            _history.Add(ChatMessage.System(_options.SystemPrompt));
        }
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            yield return new AgentRunCompleted(0, AgentRunStatus.Cancelled, "Run cancelled before start.");
            yield break;
        }

        _history.Add(ChatMessage.User(userPrompt));

        var toolDefs = _tools.Values.Select(t => new ToolDefinition(
            t.Name,
            t.Description,
            t.ParametersSchema
        )).ToList();

        int turn = 0;
        while (!ct.IsCancellationRequested)
        {
            if (turn >= _options.MaxTurns)
            {
                yield return new AgentRunCompleted(
                    turn, 
                    AgentRunStatus.MaxTurnsReached, 
                    $"Reached maximum turns limit of {_options.MaxTurns}."
                );
                yield break;
            }

            turn++;
            yield return new AgentTurnStarted(turn);

            // 1. Context compaction to prevent context window explosion
            _compactor.CompactIfNeeded(_history);

            var textBuilder = new StringBuilder();
            var toolCallsMap = new Dictionary<int, (string? id, string? name, StringBuilder args)>();

            IAsyncEnumerator<ChatStreamChunk>? enumerator = null;
            Exception? streamError = null;
            bool wasCancelled = false;

            try
            {
                var stream = _llmClient.StreamChatAsync(_history, toolDefs, _options.ChatOptions, ct);
                enumerator = stream.GetAsyncEnumerator(ct);

                while (!ct.IsCancellationRequested)
                {
                    bool hasNext = false;
                    ChatStreamChunk? chunk = null;

                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                        if (hasNext)
                        {
                            chunk = enumerator.Current;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        wasCancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        streamError = ex;
                        break;
                    }

                    if (!hasNext || chunk == null)
                    {
                        break;
                    }

                    // Yielding outside try-catch block
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
            }
            finally
            {
                if (enumerator != null)
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch { }
                }
            }

            if (wasCancelled || ct.IsCancellationRequested)
            {
                yield return new AgentRunCompleted(turn, AgentRunStatus.Cancelled, "Operation was cancelled by user.");
                yield break;
            }

            if (streamError != null)
            {
                yield return new AgentErrorOccurred(streamError, $"LLM stream error: {streamError.Message}");
                yield return new AgentRunCompleted(turn, AgentRunStatus.Error, streamError.Message);
                yield break;
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

            // If no tools were called, this task completed normally
            if (completedToolCalls.Count == 0)
            {
                yield return new AgentRunCompleted(turn, AgentRunStatus.Completed);
                yield break;
            }

            // Check if this was the last allowed turn before executing tools
            if (turn >= _options.MaxTurns)
            {
                yield return new AgentRunCompleted(
                    turn,
                    AgentRunStatus.MaxTurnsReached,
                    $"Reached maximum turns limit ({_options.MaxTurns}) with pending tool calls."
                );
                yield break;
            }

            // Execute tools sequentially with individual try-catch guards
            foreach (var call in completedToolCalls)
            {
                if (ct.IsCancellationRequested)
                {
                    yield return new AgentRunCompleted(turn, AgentRunStatus.Cancelled, "Cancelled during tool execution.");
                    yield break;
                }

                yield return new ToolCallStarting(call.Id, call.Name, call.Arguments);

                ToolResult result;
                bool toolCancelled = false;

                if (_tools.TryGetValue(call.Name, out var toolInstance))
                {
                    try
                    {
                        result = await toolInstance.ExecuteAsync(call.Arguments, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        toolCancelled = true;
                        result = ToolResult.Error("Tool execution was cancelled.");
                    }
                    catch (Exception ex)
                    {
                        result = ToolResult.Error($"Unhandled exception in tool '{call.Name}': {ex.Message}");
                    }
                }
                else
                {
                    result = ToolResult.Error($"Unknown tool '{call.Name}'.");
                }

                if (toolCancelled)
                {
                    yield return new AgentRunCompleted(turn, AgentRunStatus.Cancelled, "Tool execution was cancelled.");
                    yield break;
                }

                _history.Add(ChatMessage.ToolResult(call.Id, result.Content, call.Name));
                yield return new ToolCallCompleted(call.Id, call.Name, result);
            }
        }

        yield return new AgentRunCompleted(turn, AgentRunStatus.Completed);
    }
}
