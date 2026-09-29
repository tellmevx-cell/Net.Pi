namespace Net.Pi.Tools;

public static class PathGuard
{
    public static (bool IsAllowed, string FullPath, string? ErrorMessage) ResolveAndValidate(
        string workspaceRoot,
        string inputPath,
        bool allowOutsideWorkspace = false)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return (false, "", "Path cannot be empty.");
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetPath = Path.IsPathRooted(inputPath)
                ? Path.GetFullPath(inputPath)
                : Path.GetFullPath(Path.Combine(normalizedRoot, inputPath));

            if (!allowOutsideWorkspace)
            {
                var rel = Path.GetRelativePath(normalizedRoot, targetPath);
                if (rel.Equals("..", StringComparison.Ordinal) ||
                    rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    rel.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                    Path.IsPathRooted(rel)) // e.g. different drive letters on Windows
                {
                    return (false, targetPath, $"Access denied: Path '{inputPath}' resolves outside workspace '{workspaceRoot}'. Path traversal is prohibited by PathGuard.");
                }
            }

            return (true, targetPath, null);
        }
        catch (Exception ex)
        {
            return (false, "", $"Invalid path '{inputPath}': {ex.Message}");
        }
    }
}
