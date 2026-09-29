using Net.Pi.Core.Skills;
using Net.Pi.Tools;
using Xunit;

namespace Net.Pi.Tests;

public class SkillsAndWebTests : IDisposable
{
    private readonly string _tempSkillsDir;

    public SkillsAndWebTests()
    {
        _tempSkillsDir = Path.Combine(Path.GetTempPath(), "net_pi_skills_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempSkillsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempSkillsDir))
        {
            try { Directory.Delete(_tempSkillsDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SkillManager_LoadsAndReadsSkill()
    {
        var skillFolder = Path.Combine(_tempSkillsDir, "test-expert");
        Directory.CreateDirectory(skillFolder);

        var skillContent = """
        ---
        name: test-expert
        description: "A test specialized skill for unit testing"
        ---
        # Test Expert Body
        Step 1: Do something.
        """;
        await File.WriteAllTextAsync(Path.Combine(skillFolder, "SKILL.md"), skillContent);

        var manager = new SkillManager();
        manager.LoadSkillsFromDirectory(_tempSkillsDir);

        Assert.Single(manager.Skills);
        var skill = manager.GetSkill("test-expert");
        Assert.NotNull(skill);
        Assert.Equal("test-expert", skill.Name);
        Assert.Equal("A test specialized skill for unit testing", skill.Description);
        Assert.Contains("Step 1: Do something.", skill.Body);

        var readSkillTool = new ReadSkillTool(manager);
        var result = await readSkillTool.ExecuteAsync("{\"name\":\"test-expert\"}");
        Assert.False(result.IsError);
        Assert.Contains("Test Expert Body", result.Content);
    }
}
