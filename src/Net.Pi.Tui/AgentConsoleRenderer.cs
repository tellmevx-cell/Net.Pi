using Net.Pi.Core;
using Net.Pi.Core.Events;

namespace Net.Pi.Tui;

public class AgentConsoleRenderer
{
    private bool _hasWrittenAssistantHeader = false;
    private bool _hasWrittenThinkingHeader = false;

    public async Task RenderStreamAsync(IAsyncEnumerable<AgentEvent> events, CancellationToken ct = default)
    {
        _hasWrittenAssistantHeader = false;
        _hasWrittenThinkingHeader = false;

        await foreach (var evt in events.WithCancellation(ct))
        {
            switch (evt)
            {
                case AgentTurnStarted:
                    _hasWrittenAssistantHeader = false;
                    _hasWrittenThinkingHeader = false;
                    break;

                case AgentReasoningDelta reasoning:
                    if (!_hasWrittenThinkingHeader)
                    {
                        Console.WriteLine();
                        Console.WriteLine(Ansi.Color("💭 [Thinking Process]", Ansi.Gray + Ansi.Italic));
                        _hasWrittenThinkingHeader = true;
                    }
                    Console.Write(Ansi.GrayText(reasoning.Delta));
                    break;

                case AgentTextDelta text:
                    if (_hasWrittenThinkingHeader && !_hasWrittenAssistantHeader)
                    {
                        Console.WriteLine();
                    }
                    if (!_hasWrittenAssistantHeader)
                    {
                        Console.WriteLine();
                        Console.Write(Ansi.Color("🤖 Pi > ", Ansi.Cyan + Ansi.Bold));
                        _hasWrittenAssistantHeader = true;
                    }
                    Console.Write(text.Delta);
                    break;

                case ToolCallStarting toolStart:
                    Console.WriteLine();
                    Console.WriteLine(Ansi.Color($"⚡ [Tool Call] {toolStart.ToolName}", Ansi.Yellow + Ansi.Bold));
                    var args = toolStart.ArgumentsJson;
                    if (args.Length > 200)
                    {
                        args = args[..200] + "...";
                    }
                    Console.WriteLine(Ansi.GrayText($"   Args: {args}"));
                    break;

                case ToolCallCompleted toolDone:
                    var status = toolDone.Result.IsError ? Ansi.RedText("✗ FAILED") : Ansi.GreenText("✓ OK");
                    Console.WriteLine($"   Result ({status}):");
                    var content = toolDone.Result.Content;
                    var lines = content.Split('\n');
                    var displayLines = lines.Take(10);
                    foreach (var l in displayLines)
                    {
                        Console.WriteLine(Ansi.GrayText($"     | {l.TrimEnd()}"));
                    }
                    if (lines.Length > 10)
                    {
                        Console.WriteLine(Ansi.GrayText($"     | ... ({lines.Length - 10} more lines omitted)"));
                    }
                    _hasWrittenAssistantHeader = false;
                    break;

                case AgentTurnCompleted turnDone:
                    if (turnDone.Usage != null)
                    {
                        var cost = turnDone.Usage.EstimateCostUsd("gemini");
                        var timeStr = turnDone.Duration.HasValue ? $"{turnDone.Duration.Value.TotalSeconds:F1}s" : "-";
                        Console.WriteLine();
                        Console.WriteLine(Ansi.GrayText($"   ⚡ [Turn {turnDone.TurnIndex} finished in {timeStr} | Tokens: {turnDone.Usage.TotalTokens} (p:{turnDone.Usage.PromptTokens}, c:{turnDone.Usage.CompletionTokens}) | est: ~${cost:F5}]"));
                    }
                    break;

                case AgentErrorOccurred err:
                    Console.WriteLine();
                    Console.WriteLine(Ansi.RedText($"[ERROR] {err.Message}"));
                    break;

                case AgentRunCompleted runDone:
                    Console.WriteLine();
                    if (runDone.Status == AgentRunStatus.Cancelled)
                    {
                        Console.WriteLine(Ansi.YellowText($"\n⏹  Execution cancelled ({runDone.Message ?? "by user"})."));
                    }
                    else if (runDone.Status == AgentRunStatus.MaxTurnsReached)
                    {
                        Console.WriteLine(Ansi.YellowText($"\n⚠️  Max turns limit reached ({runDone.TotalTurns} turns). Send a new prompt to continue."));
                    }
                    else if (runDone.Status == AgentRunStatus.Error)
                    {
                        Console.WriteLine(Ansi.RedText($"\n✗  Run stopped with error: {runDone.Message}"));
                    }
                    break;
            }
        }
    }
}
