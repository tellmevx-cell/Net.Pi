using System.Text.Json.Nodes;
using Net.Pi.Core;
using Net.Pi.Core.Skills;

namespace Net.Pi.Tools;

public class ReadSkillTool : ITool
{
    private readonly SkillManager _skillManager;

    public string Name => "read_skill";
    public string Description => "Fetches the full specialized instructions and workflow for a skill listed in Available Skills.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "The exact skill name from the Available Skills catalog" }
        },
        required = new[] { "name" }
    };

    public ReadSkillTool(SkillManager skillManager)
    {
        _skillManager = skillManager ?? throw new ArgumentNullException(nameof(skillManager));
    }

    public Task<ToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        try
        {
            var node = JsonNode.Parse(argumentsJson);
            var name = node?["name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(ToolResult.Error("Missing required parameter: 'name'."));
            }

            var skill = _skillManager.GetSkill(name);
            if (skill == null)
            {
                return Task.FromResult(ToolResult.Error($"Skill '{name}' not found. Check the Available Skills list in the prompt."));
            }

            return Task.FromResult(ToolResult.Ok(
                $"### Skill: {skill.Name}\n\n{skill.Body}",
                new { name = skill.Name, path = skill.FilePath }
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to read skill: {ex.Message}"));
        }
    }
}
