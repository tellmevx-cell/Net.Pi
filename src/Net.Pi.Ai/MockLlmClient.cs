using System.Runtime.CompilerServices;
using Net.Pi.Ai.Models;

namespace Net.Pi.Ai;

public class MockLlmClient : ILlmClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, (string Text, IReadOnlyList<ToolCall>? Tools)>> _responses = new();

    public string DefaultModel { get; set; } = "mock-model";

    public void EnqueueResponse(string text, IReadOnlyList<ToolCall>? tools = null)
    {
        _responses.Enqueue(_ => (text, tools));
    }

    public void EnqueueResponse(Func<IReadOnlyList<ChatMessage>, (string Text, IReadOnlyList<ToolCall>? Tools)> responder)
    {
        _responses.Enqueue(responder);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();

        (string Text, IReadOnlyList<ToolCall>? Tools) response;
        if (_responses.Count > 0)
        {
            response = _responses.Dequeue()(messages);
        }
        else
        {
            response = ("Default mock response.", null);
        }

        if (!string.IsNullOrEmpty(response.Text))
        {
            // Yield in words to simulate stream
            var words = response.Text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var piece = words[i] + (i < words.Length - 1 ? " " : "");
                yield return new ChatStreamChunk(piece);
            }
        }

        if (response.Tools != null && response.Tools.Count > 0)
        {
            var deltas = response.Tools.Select((t, idx) => 
                new ToolCallDelta(idx, t.Id, t.Name, t.Arguments)).ToList();
            yield return new ChatStreamChunk(ToolDeltas: deltas);
        }

        yield return new ChatStreamChunk(FinishReason: response.Tools != null && response.Tools.Count > 0 ? "tool_calls" : "stop");
    }

    public Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default)
    {
        (string Text, IReadOnlyList<ToolCall>? Tools) response;
        if (_responses.Count > 0)
        {
            response = _responses.Dequeue()(messages);
        }
        else
        {
            response = ("Default mock response.", null);
        }

        return Task.FromResult(new ChatCompletionResult(
            response.Text, 
            response.Tools ?? Array.Empty<ToolCall>(),
            response.Tools != null && response.Tools.Count > 0 ? "tool_calls" : "stop"
        ));
    }
}
