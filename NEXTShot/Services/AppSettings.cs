using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NEXTShot.Services;

public sealed class AppSettings
{
    public bool AutostartOn { get; set; } = true;
    public bool CaptureCursor { get; set; } = true;
    public bool PlaySound { get; set; } = true;
    public bool SilentMode { get; set; } = true;
    /// <summary>0 — не проверять, 1 — раз в неделю, 2 — раз в месяц.</summary>
    public int UpdateCheckInterval { get; set; } = (int)UpdateCheckIntervalKind.Weekly;

    public DateTime? LastUpdateCheckUtc { get; set; }

    public bool AlwaysShowEditor { get; set; } = false;
    public bool HighlightMouseClicks { get; set; } = true;
    public string ScreenshotsPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    public string VideoSavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
    public int JpegQuality { get; set; } = 90;
    public int ScreenshotFileType { get; set; } = 1;
    /// <summary>Макс. одновременных оверлеев «снимок в снимке» (1–7).</summary>
    public int MaxNestedRegionCaptures { get; set; } = 1;
    public int VideoQuality { get; set; } = 0;
    public bool ShowVideoControlPanel { get; set; } = true;
    public bool VideoControlPanelUseIcons { get; set; } = false;
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
        string? json = null;
        try
        {
            if (File.Exists(SettingsPath))
            {
                json = File.ReadAllText(SettingsPath);
                loaded = JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch { }

        var firstRun = loaded is null;
        var d = loaded ?? new AppSettings();
        if (loaded is not null && json is not null)
            MigrateLegacyUpdateCheck(d, json);
        else
            d.UpdateCheckInterval = (int)UpdateCheckIntervalExtensions.ClampInterval(d.UpdateCheckInterval);
        if (firstRun)
            AutostartService.Apply(d.AutostartOn);

        if (string.IsNullOrWhiteSpace(d.VideoSavePath))
            d.VideoSavePath = d.ScreenshotsPath;
        if (firstRun)
            d.Save();
        return d;
    }

    private static void MigrateLegacyUpdateCheck(AppSettings d, string json)
    {
        if (json.Contains("UpdateCheckInterval", StringComparison.OrdinalIgnoreCase))
        {
            d.UpdateCheckInterval = (int)UpdateCheckIntervalExtensions.ClampInterval(d.UpdateCheckInterval);
            return;
        }

        if (json.Contains("\"CheckForUpdates\"", StringComparison.OrdinalIgnoreCase))
        {
            var enabled = json.Contains("\"CheckForUpdates\":true", StringComparison.OrdinalIgnoreCase)
                          || json.Contains("\"CheckForUpdates\": true", StringComparison.OrdinalIgnoreCase);
            d.UpdateCheckInterval = enabled
                ? (int)UpdateCheckIntervalKind.Weekly
                : (int)UpdateCheckIntervalKind.None;
        }
        else
            d.UpdateCheckInterval = (int)UpdateCheckIntervalKind.Weekly;
    }

    public UpdateCheckIntervalKind UpdateCheckKind =>
        UpdateCheckIntervalExtensions.ClampInterval(UpdateCheckInterval);

    public bool IsAutomaticUpdateCheckDueUtc(DateTime utcNow)
    {
        var kind = UpdateCheckKind;
        if (kind == UpdateCheckIntervalKind.None) return false;
        if (LastUpdateCheckUtc is null) return true;
        return utcNow - LastUpdateCheckUtc.Value >= kind.ToPeriod();
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
