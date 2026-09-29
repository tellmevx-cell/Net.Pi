using System.Net;
using System.Text;
using Net.Pi.Ai;
using Net.Pi.Ai.Models;
using Xunit;

namespace Net.Pi.Tests;

public class SseParserTests
{
    [Fact]
    public async Task StreamChatAsync_ParsesMultiLineDataAndPreservesFinishReason()
    {
        var sseResponse = """
        : ping

        data: {"id":"1","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello "},"finish_reason":null}]}

        data: {"id":"2","choices":[{"index":0,"delta":{"content":"world!"},"finish_reason":null}]}

        data: {"id":"3","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"c1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]},"finish_reason":"tool_calls"}]}

        data: [DONE]

        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, sseResponse);
        var client = new OpenAiCompatibleClient("test-key", "http://mock-api/v1", "test-model", new HttpClient(handler));

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in client.StreamChatAsync(new[] { ChatMessage.User("Hi") }))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        var text = string.Join("", chunks.Where(c => c.DeltaText != null).Select(c => c.DeltaText));
        Assert.Equal("Hello world!", text);

        var toolChunk = chunks.FirstOrDefault(c => c.ToolDeltas != null);
        Assert.NotNull(toolChunk);
        Assert.Equal("read_file", toolChunk.ToolDeltas![0].Name);

        var finishChunk = chunks.FirstOrDefault(c => c.FinishReason != null);
        Assert.NotNull(finishChunk);
        Assert.Equal("tool_calls", finishChunk.FinishReason);
    }

    [Fact]
    public async Task StreamChatAsync_RetriesOnTransient500Error()
    {
        int attempts = 0;
        var sseSuccess = "data: {\"choices\":[{\"delta\":{\"content\":\"Recovered!\"}}]}\n\ndata: [DONE]\n\n";

        var handler = new DelegatingMockHandler((req) =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("Server busy")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseSuccess, Encoding.UTF8, "text/event-stream")
            };
        });

        var client = new OpenAiCompatibleClient("test-key", "http://mock-api/v1", "test-model", new HttpClient(handler));

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in client.StreamChatAsync(new[] { ChatMessage.User("Hi") }))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(2, attempts); // Successfully retried after first 500 failure
        Assert.Contains(chunks, c => c.DeltaText == "Recovered!");
    }

    private class MockHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var res = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(res);
        }
    }

    private class DelegatingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
