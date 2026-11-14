using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NEXTShot.Native;
using NEXTShot.Resources;
using NEXTShot.Services;
using WpfPoint = System.Windows.Point;
using DrawingBitmap = System.Drawing.Bitmap;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private enum SelInteraction { None, DrawingNew, Moving, Resizing }

    private SelInteraction _selInteraction = SelInteraction.None;
    private bool _showRegionHandles;
    private bool _regionMoveLocked;
    private int _activeHandle = -1;
    private WpfPoint _dragOrigin;
    private double _anchorSelX, _anchorSelY, _anchorSelW, _anchorSelH;
    private readonly Border[] _handles = new Border[8];
    private DrawingBitmap? _desktopSnapshot;

    private static readonly Cursor[] HandleCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE
    };

    private void InitSelectionChrome()
    {
        for (var i = 0; i < _handles.Length; i++)
        {
            var b = new Border
            {
                Width = 8,
                Height = 8,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Black,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            };
            _handles[i] = b;
            SelectionChrome.Children.Add(b);
        }
    }

    private WpfPoint[] GetHandleCenters() =>
    [
        new(_selX, _selY),
        new(_selX + _selW / 2, _selY),
        new(_selX + _selW, _selY),
        new(_selX + _selW, _selY + _selH / 2),
        new(_selX + _selW, _selY + _selH),
        new(_selX + _selW / 2, _selY + _selH),
        new(_selX, _selY + _selH),
        new(_selX, _selY + _selH / 2)
    ];

    private void UpdateHandlePositions()
    {
        if (!_showRegionHandles) return;
        var centers = GetHandleCenters();
        for (var i = 0; i < _handles.Length; i++)
        {
            Canvas.SetLeft(_handles[i], centers[i].X - 4);
            Canvas.SetTop(_handles[i], centers[i].Y - 4);
        }
        SelectionChrome.Visibility = Visibility.Visible;
    }

    private int HitTestHandle(WpfPoint p)
    {
        const double hit = 7;
        var centers = GetHandleCenters();
        for (var i = 0; i < centers.Length; i++)
        {
            if (Math.Abs(p.X - centers[i].X) <= hit && Math.Abs(p.Y - centers[i].Y) <= hit)
                return i;
        }

        const double edge = 8;
        var nearTop = p.Y >= _selY - edge && p.Y <= _selY + edge && p.X >= _selX && p.X <= _selX + _selW;
        var nearBottom = p.Y >= _selY + _selH - edge && p.Y <= _selY + _selH + edge && p.X >= _selX && p.X <= _selX + _selW;
        var nearLeft = p.X >= _selX - edge && p.X <= _selX + edge && p.Y >= _selY && p.Y <= _selY + _selH;
        var nearRight = p.X >= _selX + _selW - edge && p.X <= _selX + _selW + edge && p.Y >= _selY && p.Y <= _selY + _selH;

        if (nearTop && nearLeft) return 0;
        if (nearTop && nearRight) return 2;
        if (nearBottom && nearRight) return 4;
        if (nearBottom && nearLeft) return 6;
        if (nearTop) return 1;
        if (nearRight) return 3;
        if (nearBottom) return 5;
        if (nearLeft) return 7;

        return -1;
    }

    private bool IsInsideSelection(WpfPoint p)
    {
        if (p.X < _selX || p.X > _selX + _selW || p.Y < _selY || p.Y > _selY + _selH)
            return false;
        return HitTestHandle(p) < 0;
    }

    private bool CanMoveRegion() => _editing && !_regionMoveLocked && !_editorVideoRecording;

    private void LockRegionMove() => _regionMoveLocked = true;

    private void UpdateAdjustCursor(WpfPoint p)
    {
        var handle = HitTestHandle(p);
        if (handle >= 0)
            Cursor = HandleCursors[handle];
        else if (CanMoveRegion() && IsInsideSelection(p))
            Cursor = Cursors.SizeAll;
        else if (_editing)
            Cursor = Cursors.Arrow;
        else
            Cursor = Cursors.Cross;
    }

    private void OpenEditorForCurrentSelection()
    {
        if (_selW < 5 || _selH < 5) return;

        var screen = ToPhysicalRect(_selX, _selY, _selW, _selH);
        SelectedRect = screen;

        if (_videoSelectionOnly)
        {
            DialogResult = true;
            Close();
            return;
        }

        try
        {
            EnterEditMode(screen);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот: {ex.Message}", SkrinshoterStrings.AppName);
            Close();
        }
    }

    private void RecaptureAfterRegionChange()
    {
        if (!_editing || _selW < 5 || _selH < 5) return;

        var screen = ToPhysicalRect(_selX, _selY, _selW, _selH);
        SelectedRect = screen;

        if (_desktopSnapshot is not null)
        {
            _capturedBitmap?.Dispose();
            _capturedBitmap = CropDesktopSnapshot(screen);
        }
        else
        {
            HideWindowForScreenGrab();
            try
            {
                var captureCursor = _settings?.CaptureCursor ?? true;
                using var bmp = ScreenCaptureService.CaptureRect(screen, captureCursor);
                _capturedBitmap?.Dispose();
                _capturedBitmap = new DrawingBitmap(bmp);
            }
            finally
            {
                ShowWindowAfterScreenGrab();
            }
        }

        var source = ScreenCaptureService.ToBitmapSource(_capturedBitmap);
        source.Freeze();
        CapturedPreview.Source = source;
        ShapeCanvas.Width = _selW;
        ShapeCanvas.Height = _selH;
        MarkCanvas.Width = _selW;
        MarkCanvas.Height = _selH;
        BlurCanvas.Width = _selW;
        BlurCanvas.Height = _selH;
        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        PositionToolbars();
    }

    private void BeginAdjustMove(WpfPoint pos)
    {
        _selInteraction = SelInteraction.Moving;
        _dragOrigin = pos;
        _anchorSelX = _selX;
        _anchorSelY = _selY;
        _anchorSelW = _selW;
        _anchorSelH = _selH;
        CaptureMouse();
    }

    private void BeginAdjustResize(WpfPoint pos, int handle)
    {
        _selInteraction = SelInteraction.Resizing;
        _activeHandle = handle;
        _dragOrigin = pos;
        _anchorSelX = _selX;
        _anchorSelY = _selY;
        _anchorSelW = _selW;
        _anchorSelH = _selH;
        CaptureMouse();
    }

    private void ApplyFrozenDesktopSnapshot(DrawingBitmap desktopSnapshot)
    {
        _desktopSnapshot?.Dispose();
        _desktopSnapshot = desktopSnapshot;
        _pendingFrozenDesktop = null;
    }

    private void CaptureFrozenDesktopSnapshot()
    {
        HideWindowForScreenGrab();
        try
        {
            _desktopSnapshot?.Dispose();
            var captureCursor = _settings?.CaptureCursor ?? true;
            using var bmp = ScreenCaptureService.CaptureVirtualScreen(captureCursor);
            _desktopSnapshot = new DrawingBitmap(bmp);
        }
        finally
        {
            ShowWindowAfterScreenGrab();
        }
    }

    private void ShowFrozenDesktopBackground()
    {
        if (_desktopSnapshot is null) return;

        var source = ScreenCaptureService.ToBitmapSource(_desktopSnapshot);
        source.Freeze();
        FrozenDesktop.Source = source;
        FrozenDesktop.Width = OverlayCanvas.Width > 0 ? OverlayCanvas.Width : Width;
        FrozenDesktop.Height = OverlayCanvas.Height > 0 ? OverlayCanvas.Height : Height;
        Canvas.SetLeft(FrozenDesktop, 0);
        Canvas.SetTop(FrozenDesktop, 0);
        FrozenDesktop.Visibility = Visibility.Visible;
    }

    private DrawingBitmap CropDesktopSnapshot(NativeMethods.Rect screen)
    {
        if (_desktopSnapshot is null)
            throw new InvalidOperationException();

        var vs = ScreenCaptureService.GetVirtualScreenRect();
        var cx = screen.Left - vs.Left;
        var cy = screen.Top - vs.Top;
        var cw = screen.Width;
        var ch = screen.Height;
        if (cx < 0 || cy < 0 || cx + cw > _desktopSnapshot.Width || cy + ch > _desktopSnapshot.Height)
            throw new ArgumentOutOfRangeException(nameof(screen), "Область выходит за пределы снимка экрана.");

        return _desktopSnapshot.Clone(new Rectangle(cx, cy, cw, ch), _desktopSnapshot.PixelFormat);
    }

    private void UpdateLivePreviewFromSnapshot()
    {
        if (_editorVideoRecording || !_editing || _desktopSnapshot is null || _selW < 5 || _selH < 5)
            return;

        var screen = ToPhysicalRect(_selX, _selY, _selW, _selH);
        var vs = ScreenCaptureService.GetVirtualScreenRect();
        var cx = screen.Left - vs.Left;
        var cy = screen.Top - vs.Top;
        var cw = screen.Width;
        var ch = screen.Height;
        if (cx < 0 || cy < 0 || cx + cw > _desktopSnapshot.Width || cy + ch > _desktopSnapshot.Height)
            return;

        using var crop = _desktopSnapshot.Clone(new Rectangle(cx, cy, cw, ch), _desktopSnapshot.PixelFormat);
        var source = ScreenCaptureService.ToBitmapSource(crop);
        source.Freeze();
        CapturedPreview.Source = source;
        ShapeCanvas.Width = _selW;
        ShapeCanvas.Height = _selH;
        MarkCanvas.Width = _selW;
        MarkCanvas.Height = _selH;
        BlurCanvas.Width = _selW;
        BlurCanvas.Height = _selH;
    }

    private void MoveSelection(WpfPoint pos)
    {
        var dx = pos.X - _dragOrigin.X;
        var dy = pos.Y - _dragOrigin.Y;
        var cw = OverlayCanvas.Width > 0 ? OverlayCanvas.Width : Width;
        var ch = OverlayCanvas.Height > 0 ? OverlayCanvas.Height : Height;
        var x = Math.Clamp(_anchorSelX + dx, 0, Math.Max(0, cw - _anchorSelW));
        var y = Math.Clamp(_anchorSelY + dy, 0, Math.Max(0, ch - _anchorSelH));
        _selX = x;
        _selY = y;
        UpdateSelection(new WpfPoint(x, y), new WpfPoint(x + _selW, y + _selH));
        if (_editing)
            UpdateLivePreviewFromSnapshot();
    }

    private void ResizeSelection(WpfPoint pos)
    {
        var dx = pos.X - _dragOrigin.X;
        var dy = pos.Y - _dragOrigin.Y;
        var x = _anchorSelX;
        var y = _anchorSelY;
        var w = _anchorSelW;
        var h = _anchorSelH;

        switch (_activeHandle)
        {
            case 0: x += dx; y += dy; w -= dx; h -= dy; break;
            case 1: y += dy; h -= dy; break;
            case 2: y += dy; w += dx; h -= dy; break;
            case 3: w += dx; break;
            case 4: w += dx; h += dy; break;
            case 5: h += dy; break;
            case 6: x += dx; w -= dx; h += dy; break;
            case 7: x += dx; w -= dx; break;
        }

        NormalizeSelection(ref x, ref y, ref w, ref h);
        ClampSelectionToCanvas(ref x, ref y, ref w, ref h);
        if (w < 5 || h < 5) return;

        _selX = x;
        _selY = y;
        _selW = w;
        _selH = h;
        UpdateSelection(new WpfPoint(x, y), new WpfPoint(x + w, y + h));
        if (_editing)
            UpdateLivePreviewFromSnapshot();
    }

    private static void NormalizeSelection(ref double x, ref double y, ref double w, ref double h)
    {
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
    }

    private void ClampSelectionToCanvas(ref double x, ref double y, ref double w, ref double h)
    {
        var cw = OverlayCanvas.Width > 0 ? OverlayCanvas.Width : Width;
        var ch = OverlayCanvas.Height > 0 ? OverlayCanvas.Height : Height;
        if (x < 0) { w += x; x = 0; }
        if (y < 0) { h += y; y = 0; }
        if (x + w > cw) w = cw - x;
        if (y + h > ch) h = ch - y;
        w = Math.Max(0, w);
        h = Math.Max(0, h);
    }

    private void EndAdjustInteraction()
    {
        if (_selW < 5 || _selH < 5)
        {
            _showRegionHandles = false;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            SelectionChrome.Visibility = Visibility.Collapsed;
        }

        _selInteraction = SelInteraction.None;
        _activeHandle = -1;
        ReleaseMouseCapture();
        UpdateHandlePositions();
    }
}
