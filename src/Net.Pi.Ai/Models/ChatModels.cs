using System.Text.Json.Serialization;

namespace Net.Pi.Ai.Models;

public record UsageStats(
    int PromptTokens = 0,
    int CompletionTokens = 0,
    int ReasoningTokens = 0,
    int TotalTokens = 0
)
{
    public double EstimateCostUsd(string model)
    {
        // General blended price approximations per 1M tokens
        double promptRate = model.Contains("gemini", StringComparison.OrdinalIgnoreCase) ? 0.15 : 0.50;
        double completionRate = model.Contains("gemini", StringComparison.OrdinalIgnoreCase) ? 0.60 : 1.50;

        return (PromptTokens * promptRate + CompletionTokens * completionRate) / 1_000_000.0;
    }
}

public record ToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments
);

public record ToolCallDelta(
    int Index,
    string? Id = null,
    string? Name = null,
    string? ArgumentsDelta = null
);

public record ChatMessage(
    string Role,
    string? Content = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? Name = null
)
{
    public static ChatMessage System(string content) => new("system", content);
    public static ChatMessage User(string content) => new("user", content);
    public static ChatMessage Assistant(string content, IReadOnlyList<ToolCall>? toolCalls = null) => 
        new("assistant", content, toolCalls);
    public static ChatMessage ToolResult(string toolCallId, string content, string? name = null) => 
        new("tool", content, ToolCallId: toolCallId, Name: name);
}

public record ToolDefinition(
    string Name,
    string Description,
    object ParametersSchema
);

public record ChatCompletionOptions(
    string? Model = null,
    float? Temperature = 0.7f,
    int? MaxTokens = null,
    IReadOnlyList<string>? Stop = null
);

public record ChatStreamChunk(
    string? DeltaText = null,
    string? ReasoningDelta = null,
    IReadOnlyList<ToolCallDelta>? ToolDeltas = null,
    string? FinishReason = null,
    UsageStats? Usage = null
);

public record ChatCompletionResult(
    string Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? FinishReason = null,
    UsageStats? Usage = null
);
