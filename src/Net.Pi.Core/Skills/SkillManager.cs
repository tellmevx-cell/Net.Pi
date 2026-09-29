using System.Text;
using System.Text.RegularExpressions;

namespace Net.Pi.Core.Skills;

public record SkillDefinition(
    string Name,
    string Description,
    string FilePath,
    string Body
);

public class SkillManager
{
    private readonly Dictionary<string, SkillDefinition> _skills = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<SkillDefinition> Skills => _skills.Values;

    public void LoadSkillsFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        foreach (var dir in Directory.GetDirectories(directoryPath))
        {
            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;

            try
            {
                var skill = ParseSkillFile(skillMd, Path.GetFileName(dir));
                if (skill != null)
                {
                    _skills[skill.Name] = skill;
                }
            }
            catch
            {
                // Skip invalid skill file
            }
        }
    }

    public SkillDefinition? GetSkill(string name)
    {
        _skills.TryGetValue(name, out var skill);
        return skill;
    }

    public string BuildCatalogPrompt()
    {
        if (_skills.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n# Available Skills");
        sb.AppendLine("The following specialized skills are available. When a skill matches the user's task, call `read_skill` with its name to load full instructions before starting:\n");

        foreach (var skill in _skills.Values)
        {
            sb.AppendLine($"- `{skill.Name}` — {skill.Description}");
        }

        return sb.ToString();
    }

    private static SkillDefinition? ParseSkillFile(string filePath, string defaultName)
    {
        var content = File.ReadAllText(filePath);
        var match = Regex.Match(content, @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n(.*)$", RegexOptions.Singleline);

        string name = defaultName;
        string description = "Custom specialized skill";
        string body = content;

        if (match.Success)
        {
            var yaml = match.Groups[1].Value;
            body = match.Groups[2].Value.Trim();

            var nameMatch = Regex.Match(yaml, @"(?m)^name:\s*[""']?([^""'\r\n]+)[""']?");
            if (nameMatch.Success) name = nameMatch.Groups[1].Value.Trim();

            var descMatch = Regex.Match(yaml, @"(?m)^description:\s*[""']?([^""'\r\n]+)[""']?");
            if (descMatch.Success) description = descMatch.Groups[1].Value.Trim();
        }

        return new SkillDefinition(name, description, filePath, body);
    }
}
