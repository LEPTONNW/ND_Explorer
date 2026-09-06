using System.IO;
using System.Text.Json;
using ND_Explorer.Models;

namespace ND_Explorer.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(applicationData, "ND Explorer", "settings.json");
        Current = Load();
    }

    public ExplorerSettings Current { get; }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, SerializerOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
            // Settings persistence must never prevent the explorer from running.
        }
    }

    private ExplorerSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ExplorerSettings();
            }

            return JsonSerializer.Deserialize<ExplorerSettings>(File.ReadAllText(_settingsPath))
                   ?? new ExplorerSettings();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or System.Security.SecurityException)
        {
            return new ExplorerSettings();
        }
    }
}
