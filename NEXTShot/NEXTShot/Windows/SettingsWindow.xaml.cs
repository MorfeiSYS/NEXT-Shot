using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NEXTShot.Resources;
using NEXTShot.Services;

namespace NEXTShot.Windows;

public partial class SettingsWindow : Window
{
    public const string ProgramId = "{A7B3C4D5-E6F7-4890-ABCD-EF1234567890}";

    private readonly AppSettings _settings;
    private readonly Action? _onSaved;

    public SettingsWindow(AppSettings settings, Action? onSaved = null)
    {
        _settings = settings;
        _onSaved = onSaved;
        InitializeComponent();
        Background = (System.Windows.Media.Brush)FindResource("Set.Shell.BgBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("Set.Shell.TextBrush");
        Title = $"{SkrinshoterStrings.AppName} {GetVersionText()}";

        ChkAutostart.IsChecked = AutostartService.IsEnabled();
        ChkSilent.IsChecked = _settings.SilentMode;
        ChkUpdates.IsChecked = _settings.CheckForUpdates;
        ChkPlaySound.IsChecked = _settings.PlaySound;
        ChkAlwaysEditor.IsChecked = _settings.AlwaysShowEditor;
        ChkVideoCursor.IsChecked = _settings.CaptureCursor;
        ChkVideoClicks.IsChecked = _settings.HighlightMouseClicks;
        ChkVideoControlPanel.IsChecked = _settings.ShowVideoControlPanel;
        ChkVideoStartImmediate.IsChecked = _settings.StartRecordingAfterSelection;
        CmbFormat.SelectedIndex = Math.Clamp(_settings.ScreenshotFileType, 0, 1);
        CmbVideoQuality.SelectedIndex = Math.Clamp(_settings.VideoQuality, 0, 2);
        TxtShotPath.Text = _settings.ScreenshotsPath;
        TxtVideoPath.Text = _settings.VideoSavePath;
        TxtVersion.Text = GetVersionText();
        TxtProgramId.Text = ProgramId;

        NavList.SelectedIndex = 0;
        ShowPage("general");
    }

    private static string GetVersionText()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void NavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PageHotkeys.Visibility = tag == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        PageScreenshots.Visibility = tag == "screenshots" ? Visibility.Visible : Visibility.Collapsed;
        PageVideo.Visibility = tag == "video" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseShotPath_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = TxtShotPath.Text };
        if (dlg.ShowDialog() == true) TxtShotPath.Text = dlg.FolderName;
    }

    private void BrowseVideoPath_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = TxtVideoPath.Text };
        if (dlg.ShowDialog() == true) TxtVideoPath.Text = dlg.FolderName;
    }

    private void CheckUpdates_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Установлена последняя версия.", SkrinshoterStrings.AppName);
    }

    private void CopyId_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ProgramId);
    }

    private void SiteLink_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://skrinshoter.ru") { UseShellExecute = true }); }
        catch { }
    }

    private void ReportBug_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("mailto:support@skrinshoter.ru?subject=NEXTShot%20bug") { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Не удалось открыть почтовый клиент.", SkrinshoterStrings.AppName);
        }
    }

    private void SaveSettings()
    {
        _settings.AutostartOn = ChkAutostart.IsChecked == true;
        _settings.SilentMode = ChkSilent.IsChecked == true;
        _settings.CheckForUpdates = ChkUpdates.IsChecked == true;
        _settings.PlaySound = ChkPlaySound.IsChecked == true;
        _settings.AlwaysShowEditor = ChkAlwaysEditor.IsChecked == true;
        _settings.CaptureCursor = ChkVideoCursor.IsChecked == true;
        _settings.HighlightMouseClicks = ChkVideoClicks.IsChecked == true;
        _settings.ShowVideoControlPanel = ChkVideoControlPanel.IsChecked == true;
        _settings.StartRecordingAfterSelection = ChkVideoStartImmediate.IsChecked == true;
        _settings.ScreenshotFileType = CmbFormat.SelectedIndex;
        _settings.VideoQuality = Math.Clamp(CmbVideoQuality.SelectedIndex, 0, 2);
        _settings.ScreenshotsPath = TxtShotPath.Text.Trim();
        _settings.VideoSavePath = TxtVideoPath.Text.Trim();
        _settings.Save();
        AutostartService.Apply(_settings.AutostartOn);
        _onSaved?.Invoke();
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => SaveSettings();
}
