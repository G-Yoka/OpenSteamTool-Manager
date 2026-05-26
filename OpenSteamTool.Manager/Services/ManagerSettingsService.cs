using System.IO;
using System.Text.Json;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class ManagerSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSteamTool.Manager");
    private readonly string _settingsFile;
    private ManagerSettings _current = new();

    public ManagerSettingsService()
    {
        _settingsFile = Path.Combine(_settingsDirectory, "settings.json");
        Reload();
    }

    public string SettingsPath => _settingsFile;

    public ManagerSettings Current
    {
        get
        {
            lock (_sync)
            {
                return new ManagerSettings
                {
                    NetworkSourcePriority = _current.NetworkSourcePriority,
                    LastSavedAtUtc = _current.LastSavedAtUtc
                };
            }
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var settings = JsonSerializer.Deserialize<ManagerSettings>(json, JsonOptions);
                    _current = settings is null ? new ManagerSettings() : Normalize(settings);
                    return;
                }
            }
            catch
            {
                // Fall back to defaults when the settings file is unreadable.
            }

            _current = new ManagerSettings();
        }
    }

    public void SetNetworkSourcePriority(NetworkSourcePriority priority)
    {
        lock (_sync)
        {
            _current.NetworkSourcePriority = priority;
            _current.LastSavedAtUtc = DateTimeOffset.UtcNow;
            SaveUnsafe();
        }
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(_settingsDirectory);
        var json = JsonSerializer.Serialize(_current, JsonOptions);
        File.WriteAllText(_settingsFile, json);
    }

    private static ManagerSettings Normalize(ManagerSettings settings)
    {
        if (!Enum.IsDefined(typeof(NetworkSourcePriority), settings.NetworkSourcePriority))
        {
            settings.NetworkSourcePriority = NetworkSourcePriority.CdnFirst;
        }

        return settings;
    }
}
