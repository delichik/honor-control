using System.Text;
using System.Text.Json;

namespace HonorControl.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public sealed record AppSettingsSnapshot(
    AppThemePreference Theme,
    bool CloseToTray,
    bool HasShownTrayHint);

public sealed class AppSettingsService
{
    private readonly string settingsPath;

    public AppSettingsService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        settingsPath = Path.Combine(localAppData, "HonorControl", "settings.json");
    }

    public string? LastError { get; private set; }

    public AppSettingsSnapshot Load()
    {
        LastError = null;
        try
        {
            SettingsDocument settings = ReadDocument();
            AppThemePreference theme = Enum.TryParse(settings.Theme, true, out AppThemePreference parsedTheme)
                ? parsedTheme
                : AppThemePreference.System;
            return new AppSettingsSnapshot(theme, settings.CloseToTray, settings.HasShownTrayHint);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return new AppSettingsSnapshot(AppThemePreference.System, true, false);
        }
    }

    public void SaveTheme(AppThemePreference theme) => UpdateDocument(settings => settings.Theme = theme.ToString());

    public void SaveCloseToTray(bool enabled) => UpdateDocument(settings => settings.CloseToTray = enabled);

    public void MarkTrayHintShown() => UpdateDocument(settings => settings.HasShownTrayHint = true);

    private SettingsDocument ReadDocument()
    {
        if (!File.Exists(settingsPath)) return new SettingsDocument();
        return JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(settingsPath, Encoding.UTF8))
            ?? new SettingsDocument();
    }

    private void UpdateDocument(Action<SettingsDocument> update)
    {
        LastError = null;
        try
        {
            SettingsDocument settings = ReadDocument();
            update(settings);

            string? directory = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);

            string temporaryPath = settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
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
        public bool CloseToTray { get; set; } = true;
        public bool HasShownTrayHint { get; set; }
    }
}
