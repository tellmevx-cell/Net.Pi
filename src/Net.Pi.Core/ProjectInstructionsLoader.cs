namespace Net.Pi.Core;

public static class ProjectInstructionsLoader
{
    private static readonly string[] CandidateFiles =
    {
        "AGENTS.md",
        "CLAUDE.md",
        ".agents.md"
    };

    /// <summary>
    /// Searches the current working directory and its parent directories for AGENTS.md or CLAUDE.md,
    /// loading project-specific guidelines into the agent prompt.
    /// </summary>
    public static (string? Instructions, string? FoundPath) FindAndLoad(string startDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(startDirectory);
            while (dir != null)
            {
                foreach (var candidate in CandidateFiles)
                {
                    var file = Path.Combine(dir.FullName, candidate);
                    if (File.Exists(file))
                    {
                        var content = File.ReadAllText(file);
                        return (content, file);
                    }
                }

                // Stop if reached git root
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    break;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore access errors during traversal
        }

        return (null, null);
    }
}
