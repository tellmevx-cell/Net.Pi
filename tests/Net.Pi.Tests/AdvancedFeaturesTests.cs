using Net.Pi.Ai.Models;
using Net.Pi.Core;
using Net.Pi.Core.Sessions;
using Net.Pi.Tools;
using Xunit;

namespace Net.Pi.Tests;

public class AdvancedFeaturesTests : IDisposable
{
    private readonly string _tempDir;

    public AdvancedFeaturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "net_pi_adv_tests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SessionStore_CreateSaveLoadAndList_WorksProperly()
    {
        var store = new SessionStore(_tempDir);
        var session = store.CreateSession("test-model", _tempDir);

        Assert.NotEmpty(session.Id);
        session.Messages.Add(ChatMessage.User("Hello"));
        session.Messages.Add(ChatMessage.Assistant("World"));
        store.SaveSession(session);

        var loaded = store.LoadSession(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("Hello", loaded.Messages[0].Content);

        var list = store.ListSessions();
        Assert.Single(list);
        Assert.Equal(session.Id, list[0].Id);
    }

    [Fact]
    public void ProjectInstructionsLoader_FindsAndLoadsAgentsMd()
    {
        var subDir = Path.Combine(_tempDir, "src", "subproject");
        Directory.CreateDirectory(subDir);

        var agentsMdPath = Path.Combine(_tempDir, "AGENTS.md");
        File.WriteAllText(agentsMdPath, "Strict adherence to clean code rules.");

        var (content, foundPath) = ProjectInstructionsLoader.FindAndLoad(subDir);
        Assert.NotNull(content);
        Assert.Contains("Strict adherence to clean code rules.", content);
        Assert.Equal(agentsMdPath, foundPath);
    }

    [Fact]
    public async Task ExecuteCommand_ConfirmationGate_RejectsWhenDenied()
    {
        // Gate always denies
        var tool = new ExecuteCommandTool(_tempDir, _ => Task.FromResult(false));
        var result = await tool.ExecuteAsync("{\"command\":\"echo denied\"}");

        Assert.True(result.IsError);
        Assert.Contains("denied by user permission gate", result.Content);
    }

    [Fact]
    public async Task ExecuteCommand_ConfirmationGate_ExecutesWhenApproved()
    {
        // Gate approves
        var tool = new ExecuteCommandTool(_tempDir, _ => Task.FromResult(true));
        var result = await tool.ExecuteAsync("{\"command\":\"echo allowed\"}");

        Assert.False(result.IsError);
        Assert.Contains("allowed", result.Content);
    }
}
