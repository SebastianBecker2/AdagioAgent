namespace AdagioMachineAgent.Services;

internal static class PathPolicy
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsPathWithinAllowedDirectories(string candidatePath, IEnumerable<string> allowedDirectories)
    {
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        foreach (var dir in allowedDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var allowedRoot = Path.GetFullPath(dir);
            var allowedWithSeparator = Path.EndsInDirectorySeparator(allowedRoot)
                ? allowedRoot
                : allowedRoot + Path.DirectorySeparatorChar;

            if (string.Equals(normalizedCandidate, allowedRoot, PathComparison) ||
                normalizedCandidate.StartsWith(allowedWithSeparator, PathComparison))
            {
                return true;
            }
        }

        return false;
    }
}
