using System.Text;
using System.Text.Json;

namespace HonorControl.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public sealed class AppSettingsService
{
    private readonly string settingsPath;

    public AppSettingsService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        settingsPath = Path.Combine(localAppData, "HonorControl", "settings.json");
    }

    public string? LastError { get; private set; }

    public AppThemePreference LoadTheme()
    {
        LastError = null;
        try
        {
            if (!File.Exists(settingsPath)) return AppThemePreference.System;
            SettingsDocument? settings = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(settingsPath, Encoding.UTF8));
            return Enum.TryParse(settings?.Theme, true, out AppThemePreference theme) ? theme : AppThemePreference.System;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return AppThemePreference.System;
        }
    }

    public void SaveTheme(AppThemePreference theme)
    {
        LastError = null;
        try
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);

            string temporaryPath = settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(new SettingsDocument { Theme = theme.ToString() }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, settingsPath, true);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }

    private sealed class SettingsDocument
    {
        public string Theme { get; set; } = nameof(AppThemePreference.System);
    }
}
