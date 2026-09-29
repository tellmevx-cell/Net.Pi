namespace Net.Pi.Tools;

public static class PathGuard
{
    /// <summary>
    /// Validates and resolves an input path, following symbolic links and junctions
    /// to ensure the real target physically resides inside the designated workspace.
    /// </summary>
    public static (bool IsAllowed, string FullPath, string? ErrorMessage) ResolveAndValidate(
        string workspaceRoot,
        string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return (false, "", "Path cannot be empty.");
        }

        try
        {
            // 1. Strip Windows extended path prefix if present
            var cleanInput = inputPath;
            if (cleanInput.StartsWith(@"\\?\", StringComparison.Ordinal) || cleanInput.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                cleanInput = cleanInput[4..];
            }

            var normalizedRoot = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetPath = Path.IsPathRooted(cleanInput)
                ? Path.GetFullPath(cleanInput)
                : Path.GetFullPath(Path.Combine(normalizedRoot, cleanInput));

            // 2. Initial relative boundary check
            if (!IsPathInside(normalizedRoot, targetPath))
            {
                return (false, targetPath, $"Access denied: Path '{inputPath}' resolves outside workspace '{workspaceRoot}'. Path traversal is prohibited by PathGuard.");
            }

            // 3. Resolve symbolic links / junctions if the filesystem entry exists
            var realTargetPath = ResolveFinalPhysicalPath(targetPath);
            if (!IsPathInside(normalizedRoot, realTargetPath))
            {
                return (false, realTargetPath, $"Access denied: Symlink '{inputPath}' points to '{realTargetPath}', which escapes workspace '{workspaceRoot}'. Symlink breakout is prohibited by PathGuard.");
            }

            return (true, targetPath, null);
        }
        catch (Exception ex)
        {
            return (false, "", $"Invalid path '{inputPath}': {ex.Message}");
        }
    }

    private static bool IsPathInside(string root, string target)
    {
        var rel = Path.GetRelativePath(root, target);
        if (rel.Equals("..", StringComparison.Ordinal) ||
            rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            rel.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(rel))
        {
            return false;
        }
        return true;
    }

    private static string ResolveFinalPhysicalPath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.LinkTarget != null)
                {
                    var target = fi.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null) return Path.GetFullPath(target.FullName);
                }
            }
            else if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                if (di.LinkTarget != null)
                {
                    var target = di.ResolveLinkTarget(returnFinalTarget: true);
                    if (target != null) return Path.GetFullPath(target.FullName);
                }
            }
        }
        catch
        {
            // If resolving link target fails, fallback to normalized path
        }
        return path;
    }
}
