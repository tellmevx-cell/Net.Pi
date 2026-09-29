using Net.Pi.Ai.Models;

namespace Net.Pi.Ai;

public interface ILlmClient
{
    string DefaultModel { get; }

    IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default
    );

    Task<ChatCompletionResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        ChatCompletionOptions? options = null,
        CancellationToken ct = default
    );
}
