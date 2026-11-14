using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NEXTShot.Native;
using NEXTShot.Services;

using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private Storyboard? _recordBlinkStoryboard;
    private bool _editorVideoRecording;
    private bool _recordingSurfaceOpen;
    private Brush? _backgroundBeforeRecording;

    internal bool IsEditorVideoRecording => _editorVideoRecording;

    internal void PrepareRecordingSurface()
    {
        if (_recordingSurfaceOpen) return;
        _recordingSurfaceOpen = true;

        SetOverlayExcludedFromCapture(true);
        _backgroundBeforeRecording ??= Background;
        Background = Brushes.Transparent;
        FrozenDesktop.Visibility = Visibility.Collapsed;
        CapturedPreview.Visibility = Visibility.Collapsed;
        ShapeCanvas.Visibility = Visibility.Collapsed;
        MarkCanvas.Visibility = Visibility.Collapsed;
        BlurCanvas.Visibility = Visibility.Collapsed;
        HitLayer.Visibility = Visibility.Collapsed;
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionBorder.BorderBrush = Brushes.White;
        RightToolbar.Visibility = Visibility.Collapsed;

        CaptureHost.IsHitTestVisible = false;
        ShapeCanvas.IsHitTestVisible = false;
        MarkCanvas.IsHitTestVisible = false;
        BlurCanvas.IsHitTestVisible = false;
        HitLayer.IsHitTestVisible = false;

        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        PositionToolbars();
        ApplyRecordingHole();
    }

    internal void StartEditorRecordingUi()
    {
        _editorVideoRecording = true;
        _regionMoveLocked = true;
        _showRegionHandles = false;
        SelectionChrome.Visibility = Visibility.Collapsed;

        PrepareRecordingSurface();
        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        SetRecordingToolbarMode(true);
        StartRecordBlink();
        UpdateRecordButtonUi();
    }

    internal void StopEditorRecordingUi()
    {
        _editorVideoRecording = false;
        _recordingSurfaceOpen = false;

        ClearRecordingHole();
        SetOverlayExcludedFromCapture(false);
        if (_backgroundBeforeRecording is not null)
        {
            Background = _backgroundBeforeRecording;
            _backgroundBeforeRecording = null;
        }

        SelectionBorder.Visibility = Visibility.Visible;
        HitLayer.Visibility = Visibility.Visible;
        ShapeCanvas.Visibility = Visibility.Visible;
        BlurCanvas.Visibility = Visibility.Visible;
        CaptureHost.IsHitTestVisible = true;
        ShapeCanvas.IsHitTestVisible = true;
        HitLayer.IsHitTestVisible = true;
        UpdateMarkCanvasVisibility();
        RestoreCapturedPreview();
        if (_editing)
            ShowFrozenDesktopBackground();

        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        _showRegionHandles = true;
        UpdateHandlePositions();
        RightToolbar.Visibility = Visibility.Visible;
        SetRecordingToolbarMode(false);
        StopRecordBlink();
        UpdateRecordButtonUi();
    }

    private void RestoreCapturedPreview()
    {
        if (_capturedBitmap is null)
        {
            CapturedPreview.Source = null;
            CapturedPreview.Visibility = Visibility.Collapsed;
            return;
        }

        var source = ScreenCaptureService.ToBitmapSource(_capturedBitmap);
        source.Freeze();
        CapturedPreview.Source = source;
        CapturedPreview.Visibility = Visibility.Visible;
    }

    private void ApplyRecordingHole()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!NativeMethods.GetClientRect(hwnd, out var client) || client.Width <= 0 || client.Height <= 0)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var x = (int)Math.Round(_selX * dpi.DpiScaleX);
        var y = (int)Math.Round(_selY * dpi.DpiScaleY);
        var w = Math.Max(1, (int)Math.Round(_selW * dpi.DpiScaleX));
        var h = Math.Max(1, (int)Math.Round(_selH * dpi.DpiScaleY));
        x = Math.Clamp(x, 0, client.Width);
        y = Math.Clamp(y, 0, client.Height);
        w = Math.Min(w, client.Width - x);
        h = Math.Min(h, client.Height - y);
        if (w <= 0 || h <= 0) return;

        var full = NativeMethods.CreateRectRgn(0, 0, client.Width, client.Height);
        var hole = NativeMethods.CreateRectRgn(x, y, x + w, y + h);
        var diff = NativeMethods.CreateRectRgn(0, 0, 1, 1);
        var combined = NativeMethods.CombineRgn(diff, full, hole, NativeMethods.RgnDiff);
        NativeMethods.DeleteObject(full);
        NativeMethods.DeleteObject(hole);
        if (combined == 0 || NativeMethods.SetWindowRgn(hwnd, diff, true) == 0)
            NativeMethods.DeleteObject(diff);
    }

    private void ClearRecordingHole()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _ = NativeMethods.SetWindowRgn(hwnd, IntPtr.Zero, true);
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
        _desktopSnapshot?.Dispose();
        _desktopSnapshot = null;
        base.OnClosed(e);
    }

    private void SetOverlayExcludedFromCapture(bool exclude)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _ = NativeMethods.SetWindowDisplayAffinity(hwnd,
            exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
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
