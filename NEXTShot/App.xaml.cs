using System.IO;
using DrawingBitmap = System.Drawing.Bitmap;
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
    private MouseClickHighlightService _mouseClicks = null!;
    private TaskbarIcon? _tray;
    private VideoControlPanelWindow? _videoPanel;
    private SingleInstanceService? _singleInstance;
    private RegionCapturePrimingHook? _regionPriming;
    private long _lastRegionCaptureTick;
    private long _lastWindowCaptureTick;
    private long _lastFullCaptureTick;
    private long _lastVideoToggleTick;
    private int _regionCaptureNestDepth;
    private RegionCaptureWindow? _activeRegionOverlay;
    private bool _videoPanelHiddenForNested;
    private AutomaticUpdateService? _automaticUpdates;

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
        _mouseClicks = new MouseClickHighlightService();
        _regionPriming = new RegionCapturePrimingHook();
        _tray = (TaskbarIcon)FindResource("TrayIcon");
        _hotkeys.RegionCaptureRequested += () => Dispatcher.BeginInvoke(RequestRegionCapture);
        _hotkeys.WindowCaptureRequested += () => Dispatcher.BeginInvoke(RequestWindowCapture);
        _hotkeys.FullScreenCaptureRequested += () => Dispatcher.BeginInvoke(RequestFullCapture);
        _hotkeys.VideoToggleRequested += () => Dispatcher.BeginInvoke(RequestVideoToggle);
        _hotkeys.RegisterFromSettings(_settings);
        ConfigureGlobalHotkeyHook();
        if (!_hotkeys.RegionHotkeyRegistered)
        {
            _tray?.ShowBalloonTip(SkrinshoterStrings.AppName,
                "Print Screen занят системой. Используйте меню в трее или задайте другую клавишу.",
                BalloonIcon.Warning);
        }
        UpdateVideoMenuItem();
        _automaticUpdates = new AutomaticUpdateService(() => _settings);
        _automaticUpdates.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _automaticUpdates?.Dispose();
        CloseVideoPanel();
        _hotkeys.Dispose();
        _regionPriming?.Dispose();
        _mouseClicks.Dispose();
        _video.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void CaptureRegion_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RequestRegionCapture);
    private void CaptureWindow_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RequestWindowCapture);
    private void CaptureFull_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RequestFullCapture);
    private void ToggleVideo_OnClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(RequestVideoToggle);
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

    private void ReloadHotkeys()
    {
        _hotkeys.RegisterFromSettings(_settings);
        ConfigureGlobalHotkeyHook();
        _videoPanel?.ApplySettings(_settings);
        SyncVideoPanelState();
        _automaticUpdates?.OnSettingsChanged();
    }

    private void ConfigureGlobalHotkeyHook()
    {
        _regionPriming ??= new RegionCapturePrimingHook();
        _regionPriming.Configure(
            _settings,
            () => _settings.CaptureCursor,
            () => Dispatcher.BeginInvoke(RequestRegionCapture),
            () => Dispatcher.BeginInvoke(RequestWindowCapture),
            () => Dispatcher.BeginInvoke(RequestFullCapture),
            () => Dispatcher.BeginInvoke(RequestVideoToggle));
    }

    private RegionCaptureWindow CreateRegionOverlay() => new(
        _settings,
        false,
        ToggleRecordingFromEditor,
        () => _video.IsSessionActive,
        TogglePause,
        () => _video.RecordedDuration,
        () => _video.IsPaused);

    private void RequestRegionCapture()
    {
        if (!TryConsumeHotkeyGate(ref _lastRegionCaptureTick)) return;
        CaptureRegion();
    }

    private void RequestWindowCapture()
    {
        if (!TryConsumeHotkeyGate(ref _lastWindowCaptureTick)) return;
        CaptureWindow();
    }

    private void RequestFullCapture()
    {
        if (!TryConsumeHotkeyGate(ref _lastFullCaptureTick)) return;
        CaptureFull();
    }

    private void RequestVideoToggle()
    {
        if (!TryConsumeHotkeyGate(ref _lastVideoToggleTick)) return;
        ToggleVideo();
    }

    private static bool TryConsumeHotkeyGate(ref long lastTick)
    {
        var now = Environment.TickCount64;
        if (now - lastTick < 400) return false;
        lastTick = now;
        return true;
    }

    private void CaptureRegion()
    {
        var maxNested = Math.Clamp(_settings.MaxNestedRegionCaptures, 1, 7);
        if (_regionCaptureNestDepth >= maxNested)
        {
            _tray?.ShowBalloonTip(SkrinshoterStrings.AppName,
                $"Достигнут лимит вложенных снимков области ({maxNested}). Закройте текущий оверлей или увеличьте лимит в настройках.",
                BalloonIcon.Warning);
            return;
        }

        var nestedCapture = _activeRegionOverlay is not null || _video.IsSessionActive;
        if (nestedCapture)
            PrepareUiBeforeNestedDesktopCapture();

        DrawingBitmap? desktop = null;
        try
        {
            if (nestedCapture)
            {
                using var bmp = ScreenCaptureService.CaptureVirtualScreen(_settings.CaptureCursor);
                desktop = (DrawingBitmap)bmp.Clone();
            }
            else
            {
                desktop = _regionPriming?.TakePendingFrame();
                if (desktop is null)
                {
                    using var bmp = ScreenCaptureService.CaptureVirtualScreen(_settings.CaptureCursor);
                    desktop = (DrawingBitmap)bmp.Clone();
                }
            }

            RunRegionOverlay(overlay =>
            {
                overlay.StartWithFrozenDesktop(desktop!);
                desktop = null;
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот области: {ex.Message}", SkrinshoterStrings.AppName);
        }
        finally
        {
            desktop?.Dispose();
        }
    }

    private void PrepareUiBeforeNestedDesktopCapture()
    {
        _activeRegionOverlay?.SuppressChromeForNestedCapture(true);
        SetVideoPanelHiddenForNested(true);
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void SetVideoPanelHiddenForNested(bool hide)
    {
        if (_videoPanel is null) return;
        if (hide)
        {
            if (_videoPanel.Visibility == Visibility.Visible)
                _videoPanelHiddenForNested = true;
            _videoPanel.Visibility = Visibility.Collapsed;
        }
        else if (_videoPanelHiddenForNested)
        {
            _videoPanel.Visibility = Visibility.Visible;
            _videoPanelHiddenForNested = false;
            _videoPanel.ReapplyCaptureExclusion();
        }
    }

    private void RunRegionOverlay(Action<RegionCaptureWindow> prepare)
    {
        var maxNested = Math.Clamp(_settings.MaxNestedRegionCaptures, 1, 7);
        if (_regionCaptureNestDepth >= maxNested)
        {
            _tray?.ShowBalloonTip(SkrinshoterStrings.AppName,
                $"Достигнут лимит вложенных снимков области ({maxNested}). Закройте текущий оверлей или увеличьте лимит в настройках.",
                BalloonIcon.Warning);
            return;
        }

        _activeRegionOverlay?.SuppressChromeForNestedCapture(true);
        if (_video.IsSessionActive)
            SetVideoPanelHiddenForNested(true);
        var overlay = CreateRegionOverlay();
        overlay.SetParentOverlay(_activeRegionOverlay);
        _regionCaptureNestDepth++;
        var previous = _activeRegionOverlay;
        _activeRegionOverlay = overlay;
        try
        {
            prepare(overlay);
            overlay.ShowDialog();
        }
        finally
        {
            _regionCaptureNestDepth--;
            _activeRegionOverlay = previous;
            previous?.SuppressChromeForNestedCapture(false);
            SetVideoPanelHiddenForNested(false);
        }
    }

    private void ToggleRecordingFromEditor(NativeMethods.Rect rect)
    {
        if (_video.IsSessionActive)
        {
            _mouseClicks.Stop();
            if (_video.Stop() is { } path)
                _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, $"Запись сохранена:\n{path}", BalloonIcon.Info);
            CloseVideoPanel();
            UpdateVideoMenuItem();
            SyncVideoPanelState();
            return;
        }

        BeginRecording(rect);
    }

    private void TogglePause()
    {
        if (!_video.IsSessionActive) return;

        if (_video.IsPaused)
        {
            if (!_video.Resume())
            {
                MessageBox.Show("Не удалось продолжить запись.", SkrinshoterStrings.AppName);
                return;
            }

            if (_video.ActiveRegion is { } r)
                _mouseClicks.Start(_settings, r);
        }
        else
        {
            if (!_video.Pause())
                return;
            _mouseClicks.Stop();
        }

        SyncVideoPanelState();
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
            if (_settings.AlwaysShowEditor)
                OpenInRegionEditor(bmp, rect.Value);
            else
                NotifyScreenshotSaved(ScreenCaptureService.SaveScreenshotDirect(bmp, _settings));
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
            NotifyScreenshotSaved(ScreenCaptureService.SaveScreenshotDirect(bmp, _settings));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот экрана: {ex.Message}", SkrinshoterStrings.AppName);
        }
    }

    private void NotifyScreenshotSaved(string path) =>
        _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, $"Скриншот сохранён:\n{path}", BalloonIcon.Info);

    private void OpenInRegionEditor(System.Drawing.Bitmap bitmap, NativeMethods.Rect screenRect)
    {
        var clone = (System.Drawing.Bitmap)bitmap.Clone();
        try
        {
            RunRegionOverlay(overlay => overlay.StartWithExistingCapture(clone, screenRect));
        }
        finally
        {
            clone.Dispose();
        }
    }

    private void ToggleVideo()
    {
        if (_video.IsSessionActive)
        {
            _mouseClicks.Stop();
            var path = _video.Stop();
            CloseVideoPanel();
            UpdateVideoMenuItem();
            SyncVideoPanelState();
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
            if (_settings.ShowVideoControlPanel)
            {
                ShowVideoPanel();
                if (_settings.StartRecordingAfterSelection)
                    BeginRecording(rect);
            }
            else
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

        _mouseClicks.Start(_settings, region);
        SyncVideoPanelState();
        UpdateVideoMenuItem();
        _tray?.ShowBalloonTip(SkrinshoterStrings.AppName, "Запись начата", BalloonIcon.Info);
    }

    private void SyncVideoPanelState()
    {
        if (_videoPanel is null) return;
        _videoPanel.GetRecordedDuration = () => _video.RecordedDuration;
        _videoPanel.SetSessionState(_video.IsSessionActive, _video.IsRecording, _video.IsPaused);
        if (_video.IsSessionActive)
            _videoPanel.ReapplyCaptureExclusion();
    }

    private void ShowVideoPanel()
    {
        CloseVideoPanel();
        var rect = ScreenCaptureService.GetVirtualScreenRect();
        _videoPanel = new VideoControlPanelWindow();
        _videoPanel.ApplySettings(_settings);
        _videoPanel.GetRecordedDuration = () => _video.RecordedDuration;
        _videoPanel.StartRequested += () => Dispatcher.Invoke(() => BeginRecording(rect));
        _videoPanel.PauseToggleRequested += () => Dispatcher.Invoke(TogglePause);
        _videoPanel.StopRequested += () => Dispatcher.Invoke(ToggleVideo);
        _videoPanel.Cancelled += () => Dispatcher.Invoke(() => _videoPanel = null);
        _videoPanel.Closed += (_, _) =>
        {
            if (_videoPanel is not null && !_video.IsSessionActive)
                _videoPanel = null;
        };
        _videoPanel.Show();
        _videoPanel.ReapplyCaptureExclusion();
        SyncVideoPanelState();
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
            mi.Header = _video.IsSessionActive ? "Остановить запись" : "Запись видео";
    }
}