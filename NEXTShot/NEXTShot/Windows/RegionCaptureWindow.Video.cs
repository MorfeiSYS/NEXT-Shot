using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NEXTShot.Native;
using NEXTShot.Services;

using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private Storyboard? _recordBlinkStoryboard;
    private DispatcherTimer? _recordingPreviewTimer;
    private bool _editorVideoRecording;

    internal bool IsEditorVideoRecording => _editorVideoRecording;

    internal void StartEditorRecordingUi()
    {
        _editorVideoRecording = true;
        _regionMoveLocked = true;
        _showRegionHandles = false;
        SelectionChrome.Visibility = Visibility.Collapsed;

        SetOverlayExcludedFromCapture(true);
        CapturedPreview.Visibility = Visibility.Visible;
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.BorderBrush = System.Windows.Media.Brushes.White;
        RightToolbar.Visibility = Visibility.Collapsed;

        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        PositionToolbars();
        StartRecordingPreviewTimer();

        CaptureHost.IsHitTestVisible = false;
        ShapeCanvas.IsHitTestVisible = false;
        MarkCanvas.IsHitTestVisible = false;
        BlurCanvas.IsHitTestVisible = false;
        HitLayer.IsHitTestVisible = false;

        SetRecordingToolbarMode(true);
        StartRecordBlink();
        UpdateRecordButtonUi();
    }

    internal void StopEditorRecordingUi()
    {
        _editorVideoRecording = false;

        StopRecordingPreviewTimer();
        SetOverlayExcludedFromCapture(false);

        SelectionBorder.Visibility = Visibility.Visible;
        CaptureHost.IsHitTestVisible = true;
        ShapeCanvas.IsHitTestVisible = true;
        HitLayer.IsHitTestVisible = true;
        UpdateMarkCanvasVisibility();

        if (_capturedBitmap is not null)
        {
            CapturedPreview.Visibility = Visibility.Visible;
        }

        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        _showRegionHandles = true;
        UpdateHandlePositions();
        RightToolbar.Visibility = Visibility.Visible;
        SetRecordingToolbarMode(false);
        StopRecordBlink();
        UpdateRecordButtonUi();
    }

    private void SetRecordingToolbarMode(bool recording)
    {
        foreach (var child in VisualTreeHelpers.EnumerateChildren(BottomToolbar))
        {
            if (child is not FrameworkElement fe) continue;
            if (fe == BtnRecord || fe.Name == "BtnRecord") continue;
            if (child is Button btn && btn != BtnRecord)
                btn.IsEnabled = !recording;
        }

        if (recording)
            BottomToolbar.Cursor = Cursors.Arrow;
    }

    private void StartRecordBlink()
    {
        StopRecordBlink();
        var anim = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        _recordBlinkStoryboard = new Storyboard();
        Storyboard.SetTarget(anim, RecordDot);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
        _recordBlinkStoryboard.Children.Add(anim);
        _recordBlinkStoryboard.Begin();
    }

    private void StopRecordBlink()
    {
        if (_recordBlinkStoryboard is null) return;
        _recordBlinkStoryboard.Stop();
        _recordBlinkStoryboard = null;
        RecordDot.Opacity = 1;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopEditorRecordingUi();
        base.OnClosed(e);
    }

    private void SetOverlayExcludedFromCapture(bool exclude)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _ = NativeMethods.SetWindowDisplayAffinity(hwnd,
            exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    private void StartRecordingPreviewTimer()
    {
        StopRecordingPreviewTimer();
        _recordingPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _recordingPreviewTimer.Tick += OnRecordingPreviewTick;
        _recordingPreviewTimer.Start();
        UpdateRecordingAreaPreview();
    }

    private void StopRecordingPreviewTimer()
    {
        if (_recordingPreviewTimer is null) return;
        _recordingPreviewTimer.Stop();
        _recordingPreviewTimer = null;
    }

    private void OnRecordingPreviewTick(object? sender, EventArgs e) => UpdateRecordingAreaPreview();

    private void UpdateRecordingAreaPreview()
    {
        if (!_editorVideoRecording || _selW < 5 || _selH < 5) return;
        try
        {
            var screen = ToPhysicalRect(_selX, _selY, _selW, _selH);
            var captureCursor = _settings?.CaptureCursor ?? false;
            using var crop = ScreenCaptureService.CaptureRect(screen, captureCursor);
            var source = ScreenCaptureService.ToBitmapSource(crop);
            source.Freeze();
            CapturedPreview.Source = source;
        }
        catch
        {
            // ignore preview errors
        }
    }

}

internal static class VisualTreeHelpers
{
    public static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in EnumerateChildren(child))
                yield return nested;
        }
    }
}
