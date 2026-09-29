using System.Runtime.CompilerServices;
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
        Assert.Contains(events, e => e is AgentRunCompleted c && c.Status == AgentRunStatus.Completed);
        Assert.Equal(2, loop.History.Count);
        Assert.Equal("Hello from Net.Pi!", loop.History[1].Content);
    }

    [Fact]
    public async Task AgentLoop_WithToolCalling_ExecutesToolAndContinues()
    {
        var mockLlm = new MockLlmClient();
        mockLlm.EnqueueResponse("Invoking mock tool", new[]
        {
            new ToolCall("call_1", "mock_tool", "{\"value\":\"abc\"}")
        });
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
        Assert.Contains(events, e => e is AgentRunCompleted c && c.Status == AgentRunStatus.Completed);
        Assert.Equal(4, loop.History.Count);
        Assert.Equal("tool", loop.History[2].Role);
    }

    [Fact]
    public async Task AgentLoop_WhenLlmThrowsNetworkException_CatchesGracefullyAndEmitsAgentError()
    {
        // Verified regression test for P0: Network/401/Stream exceptions must not crash process
        var faultingLlm = new FaultingLlmClient(new HttpRequestException("Connection refused (127.0.0.1:9)"));
        var loop = new AgentLoop(faultingLlm);
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync("Hello"))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is AgentErrorOccurred err && err.Message.Contains("Connection refused"));
        var runCompleted = events.OfType<AgentRunCompleted>().FirstOrDefault();
        Assert.NotNull(runCompleted);
        Assert.Equal(AgentRunStatus.Error, runCompleted.Status);
    }

    [Fact]
    public async Task AgentLoop_WhenCancelled_EmitsCancelledStatus()
    {
        var mockLlm = new MockLlmClient();
        var loop = new AgentLoop(mockLlm);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before run

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunAsync("Hi", cts.Token))
        {
            events.Add(evt);
        }

        var runCompleted = events.OfType<AgentRunCompleted>().FirstOrDefault();
        Assert.NotNull(runCompleted);
        Assert.Equal(AgentRunStatus.Cancelled, runCompleted.Status);
    }

    [Fact]
    public async Task AgentLoop_MaxTurns_EmitsMaxTurnsReachedStatus()
    {
        var mockLlm = new MockLlmClient();
        // Enqueue tool call responses for both turns to exceed MaxTurns
        mockLlm.EnqueueResponse(_ => ("Calling tool 1", new[] { new ToolCall("tc1", "dummy", "{}") }));
        mockLlm.EnqueueResponse(_ => ("Calling tool 2", new[] { new ToolCall("tc2", "dummy", "{}") }));
        var dummyTool = new FakeTool("dummy", _ => ToolResult.Ok("ok"));

        var loop = new AgentLoop(mockLlm, new[] { dummyTool }, new AgentLoopOptions { MaxTurns = 2 });
        var events = new List<AgentEvent>();

        await foreach (var evt in loop.RunAsync("Loop forever"))
        {
            events.Add(evt);
        }

        var runCompleted = events.OfType<AgentRunCompleted>().LastOrDefault();
        Assert.NotNull(runCompleted);
        Assert.Equal(AgentRunStatus.MaxTurnsReached, runCompleted.Status);
        Assert.Equal(2, runCompleted.TotalTurns);
    }

    [Fact]
    public async Task AgentLoop_WhenToolThrows_DoesNotCrashAndRecordsError()
    {
        var mockLlm = new MockLlmClient();
        mockLlm.EnqueueResponse("Calling crash tool", new[] { new ToolCall("tc", "crash_tool", "{}") });
        mockLlm.EnqueueResponse("Tool failed as expected.");

        var crashTool = new FakeTool("crash_tool", _ => throw new InvalidOperationException("Simulated tool crash"));
        var loop = new AgentLoop(mockLlm, new[] { crashTool });

        var events = new List<AgentEvent>();
        await foreach (var evt in loop.RunAsync("Execute crash tool"))
        {
            events.Add(evt);
        }

        var completedEvt = events.OfType<ToolCallCompleted>().FirstOrDefault();
        Assert.NotNull(completedEvt);
        Assert.True(completedEvt.Result.IsError);
        Assert.Contains("Simulated tool crash", completedEvt.Result.Content);
    }

    private class FaultingLlmClient(Exception exceptionToThrow) : ILlmClient
    {
        public string DefaultModel => "fault-model";

        public async IAsyncEnumerable<ChatStreamChunk> StreamChatAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            throw exceptionToThrow;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            CancellationToken ct = default)
        {
            throw exceptionToThrow;
        }
    }

    private class FakeTool(string name, Func<string, ToolResult> handler) : ITool
    {
        public string Name { get; } = name;
        public string Description => "A test tool";
        public object ParametersSchema => new { type = "object" };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            return Task.FromResult(handler(argumentsJson));
        }
    }
}
