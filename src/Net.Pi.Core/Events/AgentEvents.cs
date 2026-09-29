namespace Net.Pi.Core.Events;

public abstract record AgentEvent;

public record AgentTurnStarted(int TurnIndex) : AgentEvent;

public record AgentReasoningDelta(string Delta) : AgentEvent;

public record AgentTextDelta(string Delta) : AgentEvent;

public record ToolCallStarting(string CallId, string ToolName, string ArgumentsJson) : AgentEvent;

public record ToolCallCompleted(string CallId, string ToolName, ToolResult Result) : AgentEvent;

public record AgentTurnCompleted(int TurnIndex, string FullMessage, bool HasToolCalls) : AgentEvent;

public record AgentRunCompleted(int TotalTurns) : AgentEvent;

public record AgentErrorOccurred(Exception Exception, string Message) : AgentEvent;
