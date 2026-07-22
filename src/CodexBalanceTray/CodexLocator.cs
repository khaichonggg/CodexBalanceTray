namespace CodexBalanceTray;

internal static class CodexLocator
{
    public static string? FindExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cachedBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");

        if (Directory.Exists(cachedBinRoot))
        {
            try
            {
                var candidate = Directory
                    .EnumerateDirectories(cachedBinRoot)
                    .Select(directory => Path.Combine(directory, "codex.exe"))
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (candidate is not null)
                {
                    return candidate.FullName;
                }
            }
            catch (IOException)
            {
                // Fall through to PATH discovery.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to PATH discovery.
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "codex.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }
}
