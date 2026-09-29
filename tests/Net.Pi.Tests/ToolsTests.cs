using Net.Pi.Tools;
using Xunit;

namespace Net.Pi.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _tempDir;

    public ToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "net_pi_tests_" + Guid.NewGuid().ToString("n"));
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
    public async Task WriteAndReadFile_WorksCorrectly()
    {
        var writeTool = new WriteFileTool(_tempDir);
        var readTool = new ReadFileTool(_tempDir);

        var writeRes = await writeTool.ExecuteAsync("{\"path\":\"test.txt\", \"content\":\"Line 1\\nLine 2\"}");
        Assert.False(writeRes.IsError);

        var readRes = await readTool.ExecuteAsync("{\"path\":\"test.txt\"}");
        Assert.False(readRes.IsError);
        Assert.Contains("Line 1", readRes.Content);
        Assert.Contains("Line 2", readRes.Content);
    }

    [Fact]
    public async Task EditFile_ReplacesExactContent()
    {
        var writeTool = new WriteFileTool(_tempDir);
        var editTool = new EditFileTool(_tempDir);
        var readTool = new ReadFileTool(_tempDir);

        await writeTool.ExecuteAsync("{\"path\":\"app.cs\", \"content\":\"int x = 1;\"}");
        var editRes = await editTool.ExecuteAsync("{\"path\":\"app.cs\", \"old_string\":\"1;\", \"new_string\":\"42;\"}");
        Assert.False(editRes.IsError);

        var readRes = await readTool.ExecuteAsync("{\"path\":\"app.cs\"}");
        Assert.Contains("int x = 42;", readRes.Content);
    }

    [Fact]
    public async Task ListDir_ListsCreatedFiles()
    {
        var writeTool = new WriteFileTool(_tempDir);
        var listTool = new ListDirTool(_tempDir);

        await writeTool.ExecuteAsync("{\"path\":\"sample.json\", \"content\":\"{}\"}");
        var listRes = await listTool.ExecuteAsync("{}");

        Assert.False(listRes.IsError);
        Assert.Contains("sample.json", listRes.Content);
    }

    [Fact]
    public async Task GlobTool_MatchesNestedFiles()
    {
        var writeTool = new WriteFileTool(_tempDir);
        var globTool = new GlobTool(_tempDir);

        await writeTool.ExecuteAsync("{\"path\":\"src/Controllers/Home.cs\", \"content\":\"// Home\"}");
        await writeTool.ExecuteAsync("{\"path\":\"src/Models/User.cs\", \"content\":\"// User\"}");
        await writeTool.ExecuteAsync("{\"path\":\"readme.md\", \"content\":\"# Readme\"}");

        var globRes = await globTool.ExecuteAsync("{\"pattern\":\"**/*.cs\"}");
        Assert.False(globRes.IsError);
        Assert.Contains("src/Controllers/Home.cs", globRes.Content);
        Assert.Contains("src/Models/User.cs", globRes.Content);
        Assert.DoesNotContain("readme.md", globRes.Content);
    }

    [Fact]
    public async Task GrepTool_FindsRegexMatchesWithLineNumbers()
    {
        var writeTool = new WriteFileTool(_tempDir);
        var grepTool = new GrepTool(_tempDir);

        await writeTool.ExecuteAsync("{\"path\":\"src/config.json\", \"content\":\"{\\n  \\\"port\\\": 8080,\\n  \\\"host\\\": \\\"localhost\\\"\\n}\"}");

        var grepRes = await grepTool.ExecuteAsync("{\"pattern\":\"port.*8080\"}");
        Assert.False(grepRes.IsError);
        Assert.Contains("config.json:2:", grepRes.Content);
        Assert.Contains("\"port\": 8080", grepRes.Content);
    }
}
