using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBalanceTray;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;

    public SettingsStore(AppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                var json = File.ReadAllText(_paths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                    ?? new AppSettings();
            }
        }
        catch (JsonException exception)
        {
            _logger.Error("settings_read_failed", exception);
        }
        catch (IOException exception)
        {
            _logger.Error("settings_read_failed", exception);
        }

        return new AppSettings();
    }

    public TraySnapshot? LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_paths.SnapshotFile))
            {
                return null;
            }

            var json = File.ReadAllText(_paths.SnapshotFile);
            return JsonSerializer.Deserialize<TraySnapshot>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.Error("snapshot_read_failed", exception);
        }
        catch (IOException exception)
        {
            _logger.Error("snapshot_read_failed", exception);
        }

        return null;
    }

    public void SaveSettings(AppSettings settings) =>
        SaveJson(_paths.SettingsFile, settings, "settings_write_failed");

    public void SaveSnapshot(TraySnapshot snapshot) =>
        SaveJson(_paths.SnapshotFile, snapshot with { IsStale = false }, "snapshot_write_failed");

    private void SaveJson<T>(string path, T value, string errorEvent)
    {
        try
        {
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (IOException exception)
        {
            _logger.Error(errorEvent, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Error(errorEvent, exception);
        }
    }
}
