namespace CodexBalanceTray;

internal sealed class AppPaths
{
    public AppPaths(string? localAppData = null)
    {
        var basePath = localAppData;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        Root = Path.Combine(basePath, "CodexBalanceTray");
        Logs = Path.Combine(Root, "logs");
        IndependentCodexHome = Path.Combine(Root, "codex-home");
        SettingsFile = Path.Combine(Root, "settings.json");
        SnapshotFile = Path.Combine(Root, "last-snapshot.json");
    }

    public string Root { get; }
    public string Logs { get; }
    public string IndependentCodexHome { get; }
    public string SettingsFile { get; }
    public string SnapshotFile { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(IndependentCodexHome);
    }
}
