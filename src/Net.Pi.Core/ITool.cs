namespace Net.Pi.Core;

public record ToolResult(
    string Content,
    bool IsError = false,
    object? Metadata = null
)
{
    public static ToolResult Ok(string content, object? metadata = null) => new(content, false, metadata);
    public static ToolResult Error(string error, object? metadata = null) => new(error, true, metadata);
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}
