using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEXTShot.Services;

public sealed class AppSettings
{
    public bool AutostartOn { get; set; }
    public bool CaptureCursor { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool SilentMode { get; set; } = true;
    public bool CheckForUpdates { get; set; } = false;
    public bool AlwaysShowEditor { get; set; } = false;
    public bool HighlightMouseClicks { get; set; } = true;
    public string ScreenshotsPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    public string VideoSavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    public int JpegQuality { get; set; } = 90;
    public int ScreenshotFileType { get; set; } = 1;
    public int VideoQuality { get; set; } = 0;
    public bool ShowVideoControlPanel { get; set; } = true;
    public bool StartRecordingAfterSelection { get; set; } = false;

    public string HotkeyRegionScreenshot { get; set; } = "44";
    public string HotkeyActiveWindowScreenshot { get; set; } = "18,44";
    public string HotkeyFullScreenshot { get; set; } = "16,44";
    public string HotkeyVideoRecording { get; set; } = "17,16,44";

    [JsonIgnore]
    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXTShot");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        AppSettings? loaded = null;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                loaded = JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch { }

        var d = loaded ?? new AppSettings();
        d.AutostartOn = AutostartService.IsEnabled();
        if (string.IsNullOrWhiteSpace(d.VideoSavePath))
            d.VideoSavePath = d.ScreenshotsPath;
        if (loaded is null)
            d.Save();
        return d;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(ScreenshotsPath);
        if (!string.IsNullOrWhiteSpace(VideoSavePath))
            Directory.CreateDirectory(VideoSavePath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
