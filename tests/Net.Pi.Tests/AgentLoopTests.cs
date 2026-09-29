using Net.Pi.Ai;
using Net.Pi.Ai.Models;
using Net.Pi.Core;
using Net.Pi.Core.Events;
using Xunit;

namespace Net.Pi.Tests;

public class AgentLoopTests
{
    [Fact]
    public async Task AgentLoop_SimplePrompt_CompletesCleanly()
    {
        var mockLlm = new MockLlmClient();
        mockLlm.EnqueueResponse("Hello from Net.Pi!");

        var loop = new AgentLoop(mockLlm);
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync("Hi"))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is AgentTurnStarted);
        Assert.Contains(events, e => e is AgentRunCompleted);
        Assert.Equal(2, loop.History.Count); // User + Assistant
        Assert.Equal("Hello from Net.Pi!", loop.History[1].Content);
    }

    [Fact]
    public async Task AgentLoop_WithToolCalling_ExecutesToolAndContinues()
    {
        var mockLlm = new MockLlmClient();
        // Turn 1: LLM decides to call tool 'mock_tool'
        mockLlm.EnqueueResponse("Invoking mock tool", new[]
        {
            new ToolCall("call_1", "mock_tool", "{\"value\":\"abc\"}")
        });
        // Turn 2: LLM receives tool result and wraps up
        mockLlm.EnqueueResponse("I have finished processing the mock tool result.");

        var executed = false;
        var fakeTool = new FakeTool("mock_tool", (args) =>
        {
            executed = true;
            return ToolResult.Ok("mock_tool_output: " + args);
        });

        var loop = new AgentLoop(mockLlm, new[] { fakeTool });
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync("Please run the mock tool"))
        {
            events.Add(evt);
        }

        Assert.True(executed);
        Assert.Contains(events, e => e is ToolCallStarting t && t.ToolName == "mock_tool");
        Assert.Contains(events, e => e is ToolCallCompleted t && t.ToolName == "mock_tool");
        Assert.Contains(events, e => e is AgentRunCompleted);

        // History: User, Assistant (with tool_calls), ToolResult, Assistant (final)
        Assert.Equal(4, loop.History.Count);
        Assert.Equal("tool", loop.History[2].Role);
    }

    private class FakeTool : ITool
    {
        private readonly Func<string, ToolResult> _handler;
        public string Name { get; }
        public string Description => "A test tool";
        public object ParametersSchema => new { type = "object" };

        public FakeTool(string name, Func<string, ToolResult> handler)
        {
            Name = name;
            _handler = handler;
        }

        public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            return Task.FromResult(_handler(argumentsJson));
        }
    }
}
