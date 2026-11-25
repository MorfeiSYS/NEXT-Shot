using System.Drawing;
using DrawingBitmap = System.Drawing.Bitmap;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WpfPoint = System.Windows.Point;
using NEXTShot.Helpers;
using NEXTShot.Native;
using NEXTShot.Resources;
using NEXTShot.Services;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow : Window
{
    private readonly AppSettings? _settings;
    private readonly bool _videoSelectionOnly;
    private readonly Action<NativeMethods.Rect>? _onVideoRecord;
    private readonly Func<bool>? _isVideoSessionActive;
    private readonly Action? _onVideoPauseToggle;
    private readonly Func<TimeSpan>? _getRecordedDuration;
    private readonly Func<bool>? _isVideoPaused;

    private RegionCaptureWindow? _parentOverlay;
    private bool _chromeSuppressedForNested;

    private WpfPoint _start;
    private bool _dragging;
    private bool _editing;
    private double _selX, _selY, _selW, _selH;
    private DrawingBitmap? _capturedBitmap;
    private string _savePath = "";
    private DrawingBitmap? _pendingBitmap;
    private DrawingBitmap? _pendingFrozenDesktop;
    private NativeMethods.Rect? _pendingScreenRect;

    public NativeMethods.Rect? SelectedRect { get; private set; }
    public bool RecordFromOverlay { get; private set; }

    public RegionCaptureWindow() : this(null, false, null) { }

    public RegionCaptureWindow(
        AppSettings? settings,
        bool videoSelectionOnly,
        Action<NativeMethods.Rect>? onVideoRecord,
        Func<bool>? isVideoSessionActive = null,
        Action? onVideoPauseToggle = null,
        Func<TimeSpan>? getRecordedDuration = null,
        Func<bool>? isVideoPaused = null)
    {
        _settings = settings;
        _videoSelectionOnly = videoSelectionOnly;
        _onVideoRecord = onVideoRecord;
        _isVideoSessionActive = isVideoSessionActive;
        _onVideoPauseToggle = onVideoPauseToggle;
        _getRecordedDuration = getRecordedDuration;
        _isVideoPaused = isVideoPaused;
        InitializeComponent();
        InitSelectionChrome();
        InitCaptureToolbarIcons();
        BtnPause.Visibility = Visibility.Collapsed;
        ApplyScreenBounds();
        if (!_videoSelectionOnly)
        {
            Opacity = 0;
            Loaded += OnLoadedCaptureFrozenDesktop;
        }
    }

    private void OnLoadedCaptureFrozenDesktop(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedCaptureFrozenDesktop;
        if (_pendingBitmap is not null)
        {
            Opacity = 1;
            return;
        }

        try
        {
            if (_pendingFrozenDesktop is not null)
                ApplyFrozenDesktopSnapshot(_pendingFrozenDesktop);
            else
                CaptureFrozenDesktopSnapshot();
            ShowFrozenDesktopBackground();
            Opacity = 1;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот: {ex.Message}", SkrinshoterStrings.AppName);
            Close();
        }
    }

    public void StartWithFrozenDesktop(DrawingBitmap desktopSnapshot)
    {
        _pendingFrozenDesktop = desktopSnapshot ?? throw new ArgumentNullException(nameof(desktopSnapshot));
    }

    public void StartWithExistingCapture(DrawingBitmap bitmap, NativeMethods.Rect screenRect)
    {
        _pendingBitmap = bitmap;
        _pendingScreenRect = screenRect;
        Loaded += OnLoadedWithPendingCapture;
    }

    private void OnLoadedWithPendingCapture(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedWithPendingCapture;
        if (_pendingBitmap is null || _pendingScreenRect is not { } screen) return;
        var bitmap = _pendingBitmap;
        _pendingBitmap = null;
        _pendingScreenRect = null;

        SelectedRect = screen;
        MapPhysicalRectToOverlayFrame(screen, out _selX, out _selY, out _selW, out _selH);

        try
        {
            EnterEditModeFromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Скриншот: {ex.Message}", SkrinshoterStrings.AppName);
            Close();
        }
    }

    private void ApplyScreenBounds()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        OverlayCanvas.Width = Width;
        OverlayCanvas.Height = Height;
        HitLayer.Width = Width;
        HitLayer.Height = Height;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_editorVideoRecording && _onVideoRecord is not null && _isVideoSessionActive?.Invoke() == true)
            {
                var rect = ToPhysicalRect(_selX, _selY, _selW, _selH);
                _onVideoRecord(rect);
                StopEditorRecordingUi();
            }

            SelectedRect = null;
            DialogResult = false;
            Close();
        }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsDescendantOfToolbar(e.OriginalSource as DependencyObject)) return;

        var pos = e.GetPosition(OverlayCanvas);

        if (_editing)
        {
            if (_editorVideoRecording) return;
            var handle = HitTestHandle(pos);
            if (handle >= 0)
            {
                BeginAdjustResize(pos, handle);
                e.Handled = true;
                return;
            }

            if (CanMoveRegion() && IsInsideSelection(pos))
            {
                BeginAdjustMove(pos);
                e.Handled = true;
                return;
            }

            return;
        }

        _selInteraction = SelInteraction.DrawingNew;
        _dragging = true;
        _start = pos;
        CaptureMouse();
        SelectionBorder.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        UpdateSelection(_start, _start);
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(OverlayCanvas);

        if (_selInteraction == SelInteraction.Moving)
        {
            MoveSelection(pos);
            return;
        }

        if (_selInteraction == SelInteraction.Resizing)
        {
            ResizeSelection(pos);
            return;
        }

        if (!_editing && _dragging && _selInteraction == SelInteraction.DrawingNew)
        {
            UpdateSelection(_start, pos);
            return;
        }

        if ((_showRegionHandles || _editing) && _selInteraction == SelInteraction.None)
            UpdateAdjustCursor(pos);
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selInteraction == SelInteraction.Moving || _selInteraction == SelInteraction.Resizing)
        {
            EndAdjustInteraction();
            if (_editing)
                RecaptureAfterRegionChange();
            e.Handled = true;
            return;
        }

        if (_editing) return;

        if (!_dragging || _selInteraction != SelInteraction.DrawingNew) return;
        _dragging = false;
        ReleaseMouseCapture();

        var end = e.GetPosition(OverlayCanvas);
        _selX = Math.Min(_start.X, end.X);
        _selY = Math.Min(_start.Y, end.Y);
        _selW = Math.Abs(_start.X - end.X);
        _selH = Math.Abs(_start.Y - end.Y);

        if (_selW < 5 || _selH < 5)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            _selInteraction = SelInteraction.None;
            return;
        }

        _selInteraction = SelInteraction.None;
        UpdateSelection(new WpfPoint(_selX, _selY), new WpfPoint(_selX + _selW, _selY + _selH));
        OpenEditorForCurrentSelection();
    }

    private NativeMethods.Rect ToPhysicalRect(double x, double y, double w, double h)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = (int)Math.Round((Left + x) * dpi.DpiScaleX);
        var top = (int)Math.Round((Top + y) * dpi.DpiScaleY);
        var right = (int)Math.Round((Left + x + w) * dpi.DpiScaleX);
        var bottom = (int)Math.Round((Top + y + h) * dpi.DpiScaleY);
        return new NativeMethods.Rect { Left = left, Top = top, Right = right, Bottom = bottom };
    }

    private void EnterEditMode(NativeMethods.Rect screen)
    {
        var frameX = _selX;
        var frameY = _selY;
        var frameW = _selW;
        var frameH = _selH;

        DrawingBitmap captured;
        if (_desktopSnapshot is not null)
            captured = CropDesktopSnapshot(screen);
        else
        {
            HideWindowForScreenGrab();
            try
            {
                var captureCursor = _settings?.CaptureCursor ?? true;
                using var bmp = ScreenCaptureService.CaptureRect(screen, captureCursor);
                captured = (DrawingBitmap)bmp.Clone();
            }
            finally
            {
                ShowWindowAfterScreenGrab();
            }
        }

        ApplyCapturedBitmap(captured, frameX, frameY, frameW, frameH);
    }

    private void EnterEditModeFromBitmap(DrawingBitmap captured)
    {
        ApplyCapturedBitmap(captured, _selX, _selY, _selW, _selH);
    }

    private void ApplyCapturedBitmap(DrawingBitmap captured, double frameX, double frameY, double frameW, double frameH)
    {
        _capturedBitmap = captured;

        var dpi = VisualTreeHelper.GetDpi(this);
        frameW = captured.Width / dpi.DpiScaleX;
        frameH = captured.Height / dpi.DpiScaleY;

        if (_settings is not null)
        {
            if (_settings.PlaySound) { try { SystemSounds.Asterisk.Play(); } catch { } }
            var ext = _settings.ScreenshotFileType == 1 ? "jpg" : "png";
            _savePath = Path.Combine(_settings.ScreenshotsPath, $"Скриншот {DateTime.Now:yyyy-MM-dd HH-mm-ss}.{ext}");
        }

        var source = ScreenCaptureService.ToBitmapSource(_capturedBitmap);
        source.Freeze();
        CapturedPreview.Source = source;
        CapturedPreview.Visibility = Visibility.Visible;
        ShapeCanvas.Width = frameW;
        ShapeCanvas.Height = frameH;
        MarkCanvas.Width = frameW;
        MarkCanvas.Height = frameH;
        BlurCanvas.Width = frameW;
        BlurCanvas.Height = frameH;

        _editing = true;
        Cursor = Cursors.Arrow;
        ShowEditChrome(frameX, frameY, frameW, frameH);

        ClearToolSelection();
        ColorSwatch.Fill = new SolidColorBrush(_lineColor);
        SyncThicknessMenuChecks();
        SyncColorMenuChecks();
    }

    private void HideWindowForScreenGrab()
    {
        Opacity = 0;
        Topmost = false;
        UpdateLayout();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        System.Threading.Thread.Sleep(30);
    }

    private void ShowWindowAfterScreenGrab()
    {
        Opacity = 1;
        Topmost = true;
        UpdateLayout();
    }

    private void ShowEditChrome(double x, double y, double w, double h)
    {
        ShadeTop.Visibility = Visibility.Visible;
        ShadeLeft.Visibility = Visibility.Visible;
        ShadeRight.Visibility = Visibility.Visible;
        ShadeBottom.Visibility = Visibility.Visible;
        SelectionBorder.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        RightToolbar.Visibility = Visibility.Visible;
        BottomToolbar.Visibility = Visibility.Visible;

        _selX = x;
        _selY = y;
        _selW = w;
        _selH = h;
        UpdateSelection(new WpfPoint(x, y), new WpfPoint(x + w, y + h));
        PositionToolbars();
        _showRegionHandles = true;
        _regionMoveLocked = false;
        UpdateHandlePositions();
        UpdateRecordButtonUi();
    }

    private void UpdateRecordButtonUi()
    {
        if (_settings is null)
        {
            BtnRecord.Visibility = Visibility.Collapsed;
            BtnPause.Visibility = Visibility.Collapsed;
            return;
        }

        BtnRecord.Visibility = Visibility.Visible;
        var sessionActive = _isVideoSessionActive?.Invoke() == true || _editorVideoRecording;
        var paused = _isVideoPaused?.Invoke() == true;
        var useIcons = _settings.VideoControlPanelUseIcons;

        if (useIcons || sessionActive)
            VideoControlContent.ApplyOverlayRecord(BtnRecord, useIcons, sessionActive);
        else
            BtnRecord.Content = RecordDot;

        BtnPause.Visibility = sessionActive ? Visibility.Visible : Visibility.Collapsed;

        if (BtnRecord.Content == RecordDot)
        {
            var color = sessionActive && !paused
                ? System.Windows.Media.Color.FromRgb(0xC0, 0x22, 0x22)
                : sessionActive
                    ? System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)
                    : System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C);
            RecordDot.Background = new SolidColorBrush(color);
        }

        UpdatePauseButtonBlinkState(sessionActive, paused, useIcons);
    }

    private void UpdatePauseButtonBlinkState(bool sessionActive, bool paused, bool useIcons)
    {
        if (sessionActive && paused)
        {
            if (_pauseBlinkStoryboard is not null)
                return;
            VideoControlContent.ApplyOverlayPause(BtnPause, useIcons, paused: true);
            StartPauseButtonBlink();
            return;
        }

        if (_pauseBlinkStoryboard is not null)
            StopPauseButtonBlink();
        if (sessionActive)
            VideoControlContent.ApplyOverlayPause(BtnPause, useIcons, paused: false);
    }

    internal void SetParentOverlay(RegionCaptureWindow? parent) => _parentOverlay = parent;

    internal void SuppressChromeForNestedCapture(bool hide)
    {
        if (hide == _chromeSuppressedForNested) return;
        _chromeSuppressedForNested = hide;
        var vis = hide ? Visibility.Collapsed : Visibility.Visible;
        if (_editorVideoRecording)
        {
            BottomToolbar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            TxtRecordDuration.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (_editing || _selW >= 5)
        {
            BottomToolbar.Visibility = vis;
            RightToolbar.Visibility = vis;
            SizeBadge.Visibility = vis;
        }
    }

    private void Pause_OnClick(object sender, RoutedEventArgs e)
    {
        if (_onVideoPauseToggle is null) return;
        _onVideoPauseToggle();
        UpdateRecordButtonUi();
        RefreshRecordDurationText();
    }

    private static bool IsDescendantOfToolbar(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is FrameworkElement { Name: "RightToolbar" or "BottomToolbar" }) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void MapPhysicalRectToOverlayFrame(NativeMethods.Rect screen, out double x, out double y, out double w, out double h)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var vs = ScreenCaptureService.GetVirtualScreenRect();
        x = (screen.Left - vs.Left) / dpi.DpiScaleX;
        y = (screen.Top - vs.Top) / dpi.DpiScaleY;
        w = screen.Width / dpi.DpiScaleX;
        h = screen.Height / dpi.DpiScaleY;
    }

    private void PositionToolbars()
    {
        const double gap = 8;
        const double margin = 8;
        var cw = OverlayCanvas.Width > 0 ? OverlayCanvas.Width : Width;
        var ch = OverlayCanvas.Height > 0 ? OverlayCanvas.Height : Height;
        var capture = new Rect(_selX, _selY, _selW, _selH);

        RightToolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var rightW = Math.Max(RightToolbar.DesiredSize.Width, RightToolbar.ActualWidth);
        if (rightW < 1) rightW = 48;
        var rightH = Math.Max(RightToolbar.DesiredSize.Height, RightToolbar.ActualHeight);
        if (rightH < 1) rightH = 200;

        var (rx, ry) = PlaceOutsideCapture(
            _selX + _selW + gap, _selY, rightW, rightH, capture, cw, ch, margin,
            preferRight: true, preferBelow: false);

        BottomToolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var bottomW = Math.Max(BottomToolbar.DesiredSize.Width, BottomToolbar.ActualWidth);
        if (bottomW < 1) bottomW = 400;
        var bottomH = Math.Max(BottomToolbar.DesiredSize.Height, BottomToolbar.ActualHeight);
        if (bottomH < 1) bottomH = 48;

        var bx = _selX + _selW / 2 - bottomW / 2;
        var by = _selY + _selH + gap;
        var (bottomX, bottomY) = PlaceOutsideCapture(
            bx, by, bottomW, bottomH, capture, cw, ch, margin,
            preferRight: false, preferBelow: true);

        ResolveToolbarOverlap(
            ref rx, ref ry, rightW, rightH,
            ref bottomX, ref bottomY, bottomW, bottomH,
            gap, margin, cw, ch);

        if (NeedsCompactToolbarLayout(capture, bottomW, bottomH, rightW, rightH, bottomX, bottomY, rx, ry))
        {
            ApplyCompactToolbarLayout(capture, cw, ch, margin, gap, bottomW, bottomH, rightW, rightH,
                out bottomX, out bottomY, out rx, out ry);
        }

        Canvas.SetLeft(RightToolbar, rx);
        Canvas.SetTop(RightToolbar, ry);
        Canvas.SetLeft(BottomToolbar, bottomX);
        Canvas.SetTop(BottomToolbar, bottomY);

        if (TxtRecordDuration.Visibility == Visibility.Visible)
        {
            TxtRecordDuration.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var tw = Math.Max(TxtRecordDuration.DesiredSize.Width, TxtRecordDuration.ActualWidth);
            if (tw < 1) tw = 48;
            var tx = bottomX + bottomW / 2 - tw / 2;
            var ty = bottomY + bottomH + 4;
            Canvas.SetLeft(TxtRecordDuration, tx);
            Canvas.SetTop(TxtRecordDuration, ty);
        }
    }

    private static void ResolveToolbarOverlap(
        ref double rx, ref double ry, double rightW, double rightH,
        ref double bx, ref double by, double bottomW, double bottomH,
        double gap, double margin, double cw, double ch)
    {
        static bool Overlap(double x1, double y1, double w1, double h1, double x2, double y2, double w2, double h2) =>
            new Rect(x1, y1, w1, h1).IntersectsWith(new Rect(x2, y2, w2, h2));

        static void Clamp(ref double px, ref double py, double w, double h, double cw, double ch, double margin)
        {
            px = Math.Clamp(px, margin, Math.Max(margin, cw - w - margin));
            py = Math.Clamp(py, margin, Math.Max(margin, ch - h - margin));
        }

        if (!Overlap(rx, ry, rightW, rightH, bx, by, bottomW, bottomH))
            return;

        var maxBottomRight = rx - gap;
        if (bx + bottomW > maxBottomRight)
            bx = maxBottomRight - bottomW;
        Clamp(ref bx, ref by, bottomW, bottomH, cw, ch, margin);

        if (!Overlap(rx, ry, rightW, rightH, bx, by, bottomW, bottomH))
            return;

        var minBottomLeft = rx + rightW + gap;
        if (bx + bottomW > rx - gap && bx < minBottomLeft)
            bx = minBottomLeft;
        Clamp(ref bx, ref by, bottomW, bottomH, cw, ch, margin);

        if (!Overlap(rx, ry, rightW, rightH, bx, by, bottomW, bottomH))
            return;

        ry = by + bottomH + gap;
        Clamp(ref rx, ref ry, rightW, rightH, cw, ch, margin);

        if (!Overlap(rx, ry, rightW, rightH, bx, by, bottomW, bottomH))
            return;

        by = ry + rightH + gap;
        Clamp(ref bx, ref by, bottomW, bottomH, cw, ch, margin);
    }

    private static bool NeedsCompactToolbarLayout(
        Rect capture, double bottomW, double bottomH, double rightW, double rightH,
        double bottomX, double bottomY, double rx, double ry)
    {
        var bottom = new Rect(bottomX, bottomY, bottomW, bottomH);
        var right = new Rect(rx, ry, rightW, rightH);
        if (bottom.IntersectsWith(capture) || right.IntersectsWith(capture))
            return true;
        if (bottom.IntersectsWith(right))
            return true;
        return false;
    }

    private static void ApplyCompactToolbarLayout(
        Rect capture, double cw, double ch, double margin, double gap,
        double bottomW, double bottomH, double rightW, double rightH,
        out double bottomX, out double bottomY, out double rx, out double ry)
    {
        static void ClampBar(ref double px, ref double py, double w, double h, double cw, double ch, double margin)
        {
            px = Math.Clamp(px, margin, Math.Max(margin, cw - w - margin));
            py = Math.Clamp(py, margin, Math.Max(margin, ch - h - margin));
        }

        bottomX = capture.Left + capture.Width / 2 - bottomW / 2;
        bottomY = capture.Bottom + gap;
        if (bottomY + bottomH > ch - margin)
            bottomY = capture.Top - bottomH - gap;
        ClampBar(ref bottomX, ref bottomY, bottomW, bottomH, cw, ch, margin);

        rx = capture.Right + gap;
        ry = capture.Top;
        if (rx + rightW > cw - margin)
            rx = capture.Left - rightW - gap;
        if (rx < margin)
            rx = Math.Clamp(capture.Right + gap, margin, Math.Max(margin, cw - rightW - margin));
        ClampBar(ref rx, ref ry, rightW, rightH, cw, ch, margin);

        if (new Rect(rx, ry, rightW, rightH).IntersectsWith(capture))
        {
            ry = capture.Bottom + gap;
            if (ry + rightH > ch - margin)
                ry = capture.Top - rightH - gap;
            ClampBar(ref rx, ref ry, rightW, rightH, cw, ch, margin);
        }

        if (new Rect(rx, ry, rightW, rightH).IntersectsWith(new Rect(bottomX, bottomY, bottomW, bottomH)))
        {
            ry = bottomY - rightH - gap;
            if (ry < margin)
                ry = bottomY + bottomH + gap;
            ClampBar(ref rx, ref ry, rightW, rightH, cw, ch, margin);
        }

        if (new Rect(bottomX, bottomY, bottomW, bottomH).IntersectsWith(capture))
        {
            bottomY = capture.Top - bottomH - gap;
            if (bottomY < margin)
                bottomY = capture.Bottom + gap;
            ClampBar(ref bottomX, ref bottomY, bottomW, bottomH, cw, ch, margin);
        }
    }

    private static (double x, double y) PlaceOutsideCapture(
        double x, double y, double tw, double th, Rect capture, double cw, double ch, double margin,
        bool preferRight, bool preferBelow)
    {
        static bool IntersectsCapture(double px, double py, double w, double h, Rect cap) =>
            new Rect(px, py, w, h).IntersectsWith(cap);

        static void ClampViewport(ref double px, ref double py, double w, double h, double cw, double ch, double margin)
        {
            px = Math.Clamp(px, margin, Math.Max(margin, cw - w - margin));
            py = Math.Clamp(py, margin, Math.Max(margin, ch - h - margin));
        }

        if (preferBelow && y + th > ch - margin)
            y = capture.Top - th - 8;
        if (preferRight && x + tw > cw - margin)
            x = capture.Left - tw - 8;

        ClampViewport(ref x, ref y, tw, th, cw, ch, margin);

        if (IntersectsCapture(x, y, tw, th, capture))
        {
            var right = capture.Right + 8;
            if (right + tw <= cw - margin)
            {
                x = right;
                y = Math.Clamp(capture.Top, margin, ch - th - margin);
            }
            else
            {
                var left = capture.Left - tw - 8;
                if (left >= margin)
                {
                    x = left;
                    y = Math.Clamp(capture.Top, margin, ch - th - margin);
                }
                else
                {
                    x = Math.Clamp(capture.Right - tw, margin, cw - tw - margin);
                    y = capture.Bottom + 8;
                    if (y + th > ch - margin)
                        y = capture.Top - th - 8;
                    ClampViewport(ref x, ref y, tw, th, cw, ch, margin);
                }
            }
        }

        return (x, y);
    }

    private void UpdateSelection(WpfPoint a, WpfPoint b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        if (!_editing)
        {
            _selX = x;
            _selY = y;
            _selW = w;
            _selH = h;
        }

        var fx = _editing ? _selX : x;
        var fy = _editing ? _selY : y;
        var fw = _editing ? _selW : w;
        var fh = _editing ? _selH : h;

        var outline = _editorVideoRecording ? 1.0 : 2.0;
        SelectionBorder.BorderThickness = new Thickness(outline);
        Canvas.SetLeft(SelectionBorder, fx - outline);
        Canvas.SetTop(SelectionBorder, fy - outline);
        SelectionBorder.Width = fw + outline * 2;
        SelectionBorder.Height = fh + outline * 2;
        SizeText.Text = $"{(int)fw} × {(int)fh}";
        Canvas.SetLeft(SizeBadge, fx);
        Canvas.SetTop(SizeBadge, Math.Max(0, fy - 28));

        var cw = OverlayCanvas.Width > 0 ? OverlayCanvas.Width : Width;
        var ch = OverlayCanvas.Height > 0 ? OverlayCanvas.Height : Height;
        ShadeTop.Width = cw; ShadeTop.Height = fy;
        Canvas.SetLeft(ShadeTop, 0); Canvas.SetTop(ShadeTop, 0);
        ShadeLeft.Width = fx; ShadeLeft.Height = fh;
        Canvas.SetLeft(ShadeLeft, 0); Canvas.SetTop(ShadeLeft, fy);
        ShadeRight.Width = Math.Max(0, cw - fx - fw);
        ShadeRight.Height = fh;
        Canvas.SetLeft(ShadeRight, fx + fw); Canvas.SetTop(ShadeRight, fy);
        ShadeBottom.Width = cw;
        ShadeBottom.Height = Math.Max(0, ch - fy - fh);
        Canvas.SetLeft(ShadeBottom, 0); Canvas.SetTop(ShadeBottom, fy + fh);

        if (_editing) PositionToolbars();
        if (_showRegionHandles) UpdateHandlePositions();
    }

    private BitmapSource RenderCaptureHostToBitmap()
    {
        if (_capturedBitmap is null) throw new InvalidOperationException();
        var dpi = VisualTreeHelper.GetDpi(this);
        var pw = _capturedBitmap.Width;
        var ph = _capturedBitmap.Height;
        CaptureHost.Measure(new System.Windows.Size(_selW, _selH));
        CaptureHost.Arrange(new Rect(0, 0, _selW, _selH));
        CaptureHost.UpdateLayout();
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        rtb.Render(CaptureHost);
        rtb.Freeze();
        return rtb;
    }

    internal BitmapSource RenderForBlurCrop() => RenderCaptureHostToBitmap();

    internal static BitmapSource BakeBlurredPatch(BitmapSource crop, double w, double h, Visual dpiSource)
    {
        var tmp = new System.Windows.Controls.Image
        {
            Source = crop,
            Width = w,
            Height = h,
            Effect = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Quality }
        };
        var dpi = VisualTreeHelper.GetDpi(dpiSource);
        var pw = (int)Math.Max(1, Math.Round(w * dpi.DpiScaleX));
        var ph = (int)Math.Max(1, Math.Round(h * dpi.DpiScaleY));
        tmp.Measure(new System.Windows.Size(w, h));
        tmp.Arrange(new Rect(0, 0, w, h));
        tmp.UpdateLayout();
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        rtb.Render(tmp);
        rtb.Freeze();
        return rtb;
    }

    internal void RefreshInkLayerAfterBlur()
    {
        UpdateMarkCanvasVisibility();
        MarkCanvas.InvalidateVisual();
        MarkCanvas.UpdateLayout();
    }

    private DrawingBitmap GetMergedBitmap()
    {
        if (_capturedBitmap is null) throw new InvalidOperationException();

        var hasInk = MarkCanvas.Strokes.Count > 0;
        var hasShapes = ShapeCanvas.Children.Count > 0 || BlurCanvas.Children.Count > 0;
        if (!hasInk && !hasShapes) return (DrawingBitmap)_capturedBitmap.Clone();

        var rtb = RenderCaptureHostToBitmap();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        return new DrawingBitmap(ms);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _capturedBitmap is null) return;
        using var merged = GetMergedBitmap();
        ScreenCaptureService.SaveBitmap(merged, _savePath, _settings.JpegQuality, _settings.ScreenshotFileType == 1);
        MessageBox.Show($"Файл сохранен:\n{_savePath}", SkrinshoterStrings.AppName);
        DialogResult = true;
        Close();
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (_capturedBitmap is null) return;
        using var merged = GetMergedBitmap();
        var source = ScreenCaptureService.ToBitmapSource(merged);
        if (!ClipboardHelper.TrySetImage(source))
        {
            MessageBox.Show(ClipboardHelper.ClipboardBusyMessage, SkrinshoterStrings.AppName);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cloud_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _capturedBitmap is null) return;
        using var merged = GetMergedBitmap();
        ScreenCaptureService.SaveBitmap(merged, _savePath, _settings.JpegQuality, _settings.ScreenshotFileType == 1);
        if (!ClipboardHelper.TrySetText(_savePath))
        {
            MessageBox.Show(ClipboardHelper.ClipboardBusyMessage, SkrinshoterStrings.AppName);
            return;
        }

        MessageBox.Show($"Облако недоступно. Путь скопирован в буфер.\n{_savePath}", SkrinshoterStrings.AppName);
        DialogResult = true;
        Close();
    }

    private void Print_OnClick(object sender, RoutedEventArgs e)
    {
        if (_capturedBitmap is null) return;
        using var merged = GetMergedBitmap();
        var pd = new System.Windows.Controls.PrintDialog();
        if (pd.ShowDialog() != true) return;
        var source = ScreenCaptureService.ToBitmapSource(merged);
        pd.PrintVisual(new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Uniform }, "NEXTShot");
    }

    private void Record_OnClick(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _onVideoRecord is null) return;
        if (_selW < 5 || _selH < 5) return;

        var wasRecording = _editorVideoRecording || _isVideoSessionActive?.Invoke() == true;
        var rect = ToPhysicalRect(_selX, _selY, _selW, _selH);
        SelectedRect = rect;
        RecordFromOverlay = true;

        if (wasRecording)
        {
            _onVideoRecord(rect);
            StopEditorRecordingUi();
            DialogResult = true;
            Close();
            return;
        }

        PrepareRecordingSurface();
        UpdateLayout();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        _onVideoRecord(rect);
        if (_isVideoSessionActive?.Invoke() != true)
        {
            StopEditorRecordingUi();
            return;
        }

        StartEditorRecordingUi();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
