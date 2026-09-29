using System.Text;
using Net.Pi.Ai.Models;

namespace Net.Pi.Core;

public class ContextCompactorOptions
{
    public int MaxContextTokens { get; set; } = 32000;
    public int MaxHistoryMessages { get; set; } = 40;
    public int PreserveRecentTurns { get; set; } = 8;
    public int MaxToolResultCharacters { get; set; } = 4000;
}

public class ContextCompactor
{
    private readonly ContextCompactorOptions _options;

    public ContextCompactor(ContextCompactorOptions? options = null)
    {
        _options = options ?? new ContextCompactorOptions();
    }

    /// <summary>
    /// Compacts history in-place if token estimate or message count exceeds threshold.
    /// Preserves system prompt (if any) and latest turns intact.
    /// </summary>
    public bool CompactIfNeeded(List<ChatMessage> history)
    {
        if (history.Count <= _options.PreserveRecentTurns + 2)
        {
            return false;
        }

        var estimatedTokens = EstimateTokens(history);
        if (estimatedTokens <= _options.MaxContextTokens && history.Count <= _options.MaxHistoryMessages)
        {
            return false;
        }

        // Pass 1: Compact large historical tool results in older turns
        var cutoffIndex = Math.Max(1, history.Count - _options.PreserveRecentTurns);
        bool modified = false;

        for (int i = 0; i < cutoffIndex; i++)
        {
            var msg = history[i];
            if (msg.Role == "tool" && !string.IsNullOrEmpty(msg.Content) && msg.Content.Length > _options.MaxToolResultCharacters)
            {
                var summary = msg.Content[..500] + $"\n... [Historical output collapsed by Net.Pi context manager: {msg.Content.Length - 500} characters omitted]";
                history[i] = new ChatMessage("tool", summary, ToolCallId: msg.ToolCallId, Name: msg.Name);
                modified = true;
            }
        }

        estimatedTokens = EstimateTokens(history);
        if (estimatedTokens <= _options.MaxContextTokens && history.Count <= _options.MaxHistoryMessages)
        {
            return modified;
        }

        // Pass 2: Summarize and collapse oldest user/assistant pairs into a condensed briefing
        var systemMsg = history.FirstOrDefault(m => m.Role == "system");
        var recentStartIndex = Math.Max(systemMsg != null ? 1 : 0, history.Count - _options.PreserveRecentTurns);
        var oldSliceCount = recentStartIndex - (systemMsg != null ? 1 : 0);

        if (oldSliceCount > 2)
        {
            var summarySb = new StringBuilder();
            summarySb.AppendLine("[Summary of earlier conversation turns to preserve context budget]:");
            
            var start = systemMsg != null ? 1 : 0;
            for (int i = start; i < recentStartIndex; i++)
            {
                var m = history[i];
                if (m.Role == "user")
                {
                    var preview = m.Content?.Length > 100 ? m.Content[..100] + "..." : m.Content;
                    summarySb.AppendLine($"- User requested: {preview}");
                }
                else if (m.Role == "assistant" && !string.IsNullOrEmpty(m.Content))
                {
                    var preview = m.Content.Length > 120 ? m.Content[..120] + "..." : m.Content;
                    summarySb.AppendLine($"  Assistant performed: {preview}");
                }
            }

            var condensedMessage = ChatMessage.System(summarySb.ToString().TrimEnd());
            var preservedRecent = history.Skip(recentStartIndex).ToList();

            history.Clear();
            if (systemMsg != null) history.Add(systemMsg);
            history.Add(condensedMessage);
            history.AddRange(preservedRecent);
            return true;
        }

        return modified;
    }

    public static int EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        int chars = 0;
        foreach (var m in messages)
        {
            chars += m.Content?.Length ?? 0;
            if (m.ToolCalls != null)
            {
                foreach (var tc in m.ToolCalls)
                {
                    chars += tc.Name.Length + tc.Arguments.Length;
                }
            }
        }
        // Heuristic: ~3.5 characters per token for mixed code and natural language
        return (int)Math.Ceiling(chars / 3.5);
    }
}
