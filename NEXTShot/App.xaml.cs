using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using NEXTShot.Native;
using NEXTShot.Resources;
using NEXTShot.Services;
using NEXTShot.Windows;

namespace NEXTShot;

public partial class App : Application
{
    private AppSettings _settings = null!;
    private HotkeyService _hotkeys = null!;
    private VideoRecorderService _video = null!;
    private TaskbarIcon? _tray;
    private VideoControlPanelWindow? _videoPanel;
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsFirstInstance)
        {
            MessageBox.Show("NEXTShot уже запущен. Откройте иконку в трее.", SkrinshoterStrings.AppName);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, SkrinshoterStrings.AppName);
            args.Handled = true;
        };
        _settings = AppSettings.Load();
        AutostartService.CleanupDuplicates();
        _hotkeys = new HotkeyService();
        _video = new VideoRecorderService();
        _tray = (TaskbarIcon)FindResource("TrayIcon");
        _hotkeys.RegionCaptureRequested += () => Dispatcher.BeginInvoke(CaptureRegion);
        _hotkeys.WindowCaptureRequested += () => Dispatcher.BeginInvoke(CaptureWindow);
        _hotkeys.FullScreenCaptureRequested += () => Dispatcher.BeginInvoke(CaptureFull);
        _hotkeys.VideoToggleRequested += () => Dispatcher.BeginInvoke(ToggleVideo);
        _hotkeys.RegisterFromSettings(_settings);
        if (!_hotkeys.RegionHotkeyRegistered)
        {
            _tray?.ShowBalloonTip(SkrinshoterStrings.AppName,
                "Print Screen занят системой. Используйте меню в трее или задайте другую клавишу.",
                BalloonIcon.Warning);
        }
        UpdateVideoMenuItem();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CloseVideoPanel();
        _hotkeys.Dispose();
        _video.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void CaptureRegion_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(CaptureRegion);
    private void CaptureWindow_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(CaptureWindow);
    private void CaptureFull_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(CaptureFull);
    private void ToggleVideo_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(ToggleVideo);
    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            new SettingsWindow(_settings, ReloadHotkeys).ShowDialog();
            _hotkeys.RegisterFromSettings(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть настройки:\n{ex.Message}\n{ex.InnerException?.Message}", SkrinshoterStrings.AppName);
        }
    }
    private void Exit_OnClick(object sender, RoutedEventArgs e) => Shutdown();

    private void ReloadHotkeys() => _hotkeys.RegisterFromSettings(_settings);

    private void CaptureRegion()
    {
        try
        {
            new RegionCaptureWindow(_settings, false, ToggleRecordingFromEditor, () => _video.IsRecording).ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот области: {ex.Message}", SkrinshoterStrings.AppName);
        }
    }

    private void ToggleRecordingFromEditor(NativeMethods.Rect rect)
    {
        if (_video.IsRecording)
        {
            if (_video.Stop() is { } path)
                _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, $"Запись сохранена:\n{path}", BalloonIcon.Info);
            CloseVideoPanel();
            UpdateVideoMenuItem();
            return;
        }

        BeginRecording(rect);
    }

    private void CaptureWindow()
    {
        try
        {
            var rect = ScreenCaptureService.GetForegroundWindowRect();
            if (rect is null)
            {
                MessageBox.Show("Не удалось снять активное окно.", SkrinshoterStrings.AppName);
                return;
            }

            using var bmp = ScreenCaptureService.CaptureRect(rect.Value, _settings.CaptureCursor);
            OpenInRegionEditor(bmp, rect.Value);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот окна: {ex.Message}", SkrinshoterStrings.AppName);
        }
    }

    private void CaptureFull()
    {
        try
        {
            var rect = ScreenCaptureService.GetVirtualScreenRect();
            using var bmp = ScreenCaptureService.CaptureRect(rect, _settings.CaptureCursor);
            OpenInRegionEditor(bmp, rect);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот экрана: {ex.Message}", SkrinshoterStrings.AppName);
        }
    }

    private void OpenInRegionEditor(System.Drawing.Bitmap bitmap, NativeMethods.Rect screenRect)
    {
        var clone = (System.Drawing.Bitmap)bitmap.Clone();
        var overlay = new RegionCaptureWindow(_settings, false, ToggleRecordingFromEditor, () => _video.IsRecording);
        overlay.StartWithExistingCapture(clone, screenRect);
        overlay.ShowDialog();
    }

    private void ToggleVideo()
    {
        if (_video.IsRecording)
        {
            var path = _video.Stop();
            CloseVideoPanel();
            UpdateVideoMenuItem();
            if (path is not null)
                _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, $"Запись сохранена:\n{path}", BalloonIcon.Info);
            return;
        }

        if (_videoPanel is not null)
        {
            CloseVideoPanel();
            return;
        }

        try
        {
            var rect = ScreenCaptureService.GetVirtualScreenRect();
            ShowVideoPanel();
            if (_settings.StartRecordingAfterSelection)
                BeginRecording(rect);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Запись видео: {ex.Message}", SkrinshoterStrings.AppName);
        }
    }

    private void BeginRecording(NativeMethods.Rect region)
    {
        var path = _video.Start(_settings, region);
        if (path is null)
        {
            MessageBox.Show("Не удалось начать запись (ffmpeg). Проверьте ffmpeg.exe рядом с NEXTShot.exe.", SkrinshoterStrings.AppName);
            return;
        }

        _videoPanel?.SetRecording(true);
        UpdateVideoMenuItem();
        _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, "Запись начата", BalloonIcon.Info);
    }

    private void ShowVideoPanel()
    {
        CloseVideoPanel();
        var rect = ScreenCaptureService.GetVirtualScreenRect();
        _videoPanel = new VideoControlPanelWindow();
        _videoPanel.StartRequested += () => Dispatcher.Invoke(() => BeginRecording(rect));
        _videoPanel.StopRequested += () => Dispatcher.Invoke(ToggleVideo);
        _videoPanel.Cancelled += () => Dispatcher.Invoke(() => _videoPanel = null);
        _videoPanel.Closed += (_, _) => { if (_videoPanel is not null && !_video.IsRecording) _videoPanel = null; };
        _videoPanel.Show();
    }

    private void CloseVideoPanel()
    {
        if (_videoPanel is null) return;
        var p = _videoPanel;
        _videoPanel = null;
        p.Close();
    }

    private void UpdateVideoMenuItem()
    {
        if (_tray?.ContextMenu is not ContextMenu menu) return;
        if (menu.Items.Count > 4 && menu.Items[4] is MenuItem mi)
            mi.Header = _video.IsRecording ? "Остановить запись" : "Запись видео";
    }
}
