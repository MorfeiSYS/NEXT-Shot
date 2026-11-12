using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NEXTShot.Controls;
using WpfPoint = System.Windows.Point;

namespace NEXTShot.Windows;

public partial class RegionCaptureWindow
{
    private enum AnnotTool { None, Arrow, Line, Pencil, Marker, Rectangle, Oval, Text, Step, Blur }

    private AnnotTool _tool = AnnotTool.None;
    private double _lineWidth = 3;
    private Color _lineColor = Color.FromRgb(0xE7, 0x4C, 0x3C);
    private int _stepIndex;
    private WpfPoint? _firstClick;
    private WpfPoint? _dragStart;
    private UIElement? _previewShape;
    private readonly Stack<Action> _undo = new();
    private Button? _activeToolButton;
    private static readonly SolidColorBrush ToolActiveBg = new(Color.FromRgb(0xD6, 0xEB, 0xF9));
    private static readonly SolidColorBrush ToolActiveBorder = new(Color.FromRgb(0x9E, 0xCF, 0xF5));

    private void InitCaptureToolbarIcons()
    {
        foreach (var btn in EnumerateToolbarImageButtons())
            WireToolbarCheckedIcon(btn);
    }

    private IEnumerable<Button> EnumerateToolbarImageButtons() =>
    [
        BtnToolArrow, BtnToolLine, BtnToolPen, BtnToolMarker,
        BtnToolRect, BtnToolOval, BtnToolText, BtnToolStep, BtnToolBlur,
        BtnThickness
    ];

    private static void WireToolbarCheckedIcon(Button btn)
    {
        if (btn.Content is not string path || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return;
        CaptureToolIcon.SetCheckedSource(btn, path[..^4] + ".checked.png");
        CaptureToolIcon.SetIsSelected(btn, false);
    }

    private void ResetToolbarIconSelection()
    {
        foreach (var btn in EnumerateToolbarImageButtons())
            CaptureToolIcon.SetIsSelected(btn, false);
        _activeToolButton = null;
    }

    private void ClearToolSelection()
    {
        _tool = AnnotTool.None;
        _firstClick = null;
        _dragStart = null;
        ClearPreview();
        MarkCanvas.IsHitTestVisible = false;
        MarkCanvas.EditingMode = InkCanvasEditingMode.None;
        UpdateMarkCanvasVisibility();
        CaptureHost.Cursor = Cursors.Arrow;
        _regionMoveLocked = false;
        ResetToolbarIconSelection();
    }

    private void SelectTool(AnnotTool tool, Button? toolButton = null)
    {
        if (tool == AnnotTool.None)
        {
            ClearToolSelection();
            return;
        }

        LockRegionMove();
        _tool = tool;
        _firstClick = null;
        _dragStart = null;
        ClearPreview();
        var ink = tool is AnnotTool.Pencil or AnnotTool.Marker;
        MarkCanvas.IsHitTestVisible = ink;
        MarkCanvas.EditingMode = ink ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        UpdateMarkCanvasVisibility();
        CaptureHost.Cursor = ink ? Cursors.Pen : Cursors.Cross;
        if (ink) ApplyInkAttributes(tool == AnnotTool.Marker);
        if (toolButton != null)
            SetActiveToolButton(toolButton);
    }

    private void SetActiveToolButton(Button btn)
    {
        if (_activeToolButton != null)
            CaptureToolIcon.SetIsSelected(_activeToolButton, false);

        _activeToolButton = btn;
        CaptureToolIcon.SetIsSelected(btn, true);
    }

    private void UpdateMarkCanvasVisibility()
    {
        var ink = _tool is AnnotTool.Pencil or AnnotTool.Marker;
        MarkCanvas.Visibility = ink || MarkCanvas.Strokes.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyInkAttributes(bool marker)
    {
        var da = new DrawingAttributes
        {
            Color = marker ? Color.FromArgb(96, _lineColor.R, _lineColor.G, _lineColor.B) : _lineColor,
            Width = marker ? 14 : _lineWidth,
            Height = marker ? 14 : _lineWidth,
            FitToCurve = true
        };
        MarkCanvas.DefaultDrawingAttributes = da;
    }

    private void ToolArrow_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Arrow, sender as Button);
    private void ToolLine_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Line, sender as Button);
    private void ToolRect_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Rectangle, sender as Button);
    private void ToolOval_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Oval, sender as Button);
    private void ToolText_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Text, sender as Button);
    private void ToolStep_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Step, sender as Button);
    private void ToolBlur_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Blur, sender as Button);

    private void Pen_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Pencil, sender as Button);
    private void Marker_OnClick(object sender, RoutedEventArgs e) => SelectTool(AnnotTool.Marker, sender as Button);

    private void Thickness_OnClick(object sender, RoutedEventArgs e)
    {
        LockRegionMove();
        if (sender is Button thicknessBtn)
            SetActiveToolButton(thicknessBtn);
        if (sender is not Button btn || btn.ContextMenu is not ContextMenu menu)
            return;
        SyncThicknessMenuChecks();
        menu.PlacementTarget = btn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ThicknessPick_OnClick(object sender, RoutedEventArgs e)
    {
        LockRegionMove();
        if (sender is not MenuItem item)
            return;
        var tag = item.Tag?.ToString();
        if (tag is null || !double.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
            return;
        _lineWidth = width;
        BtnThickness.ToolTip = $"Толщина: {_lineWidth} px";
        SetThicknessMenuChecked(item);
        if (_tool is AnnotTool.Pencil or AnnotTool.Marker)
            ApplyInkAttributes(_tool == AnnotTool.Marker);
    }

    private void SyncThicknessMenuChecks()
    {
        if (BtnThickness.ContextMenu is not ContextMenu menu) return;
        foreach (var obj in menu.Items)
        {
            if (obj is not MenuItem mi) continue;
            var tag = mi.Tag?.ToString();
            mi.IsChecked = tag is not null && double.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                && Math.Abs(w - _lineWidth) < 0.01;
        }
    }

    private void SetThicknessMenuChecked(MenuItem selected)
    {
        if (BtnThickness.ContextMenu is not ContextMenu menu) return;
        foreach (var obj in menu.Items)
            if (obj is MenuItem mi)
                mi.IsChecked = ReferenceEquals(mi, selected);
    }

    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        LockRegionMove();
        if (sender is Button colorBtn)
            SetActiveToolButton(colorBtn);
        if (sender is not Button btn || btn.ContextMenu is not ContextMenu menu)
            return;
        SyncColorMenuChecks();
        menu.PlacementTarget = btn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ColorPick_OnClick(object sender, RoutedEventArgs e)
    {
        LockRegionMove();
        if (sender is not MenuItem item || item.Tag is not string hex)
            return;
        try
        {
            _lineColor = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return;
        }

        ColorSwatch.Fill = new SolidColorBrush(_lineColor);
        SetColorMenuChecked(item);
        if (_tool is AnnotTool.Pencil or AnnotTool.Marker)
            ApplyInkAttributes(_tool == AnnotTool.Marker);
    }

    private void SyncColorMenuChecks()
    {
        if (BtnColor.ContextMenu is not ContextMenu menu) return;
        foreach (var obj in menu.Items)
        {
            if (obj is not MenuItem mi || mi.Tag is not string hex) continue;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                mi.IsChecked = c.R == _lineColor.R && c.G == _lineColor.G && c.B == _lineColor.B;
            }
            catch
            {
                mi.IsChecked = false;
            }
        }
    }

    private void SetColorMenuChecked(MenuItem selected)
    {
        if (BtnColor.ContextMenu is not ContextMenu menu) return;
        foreach (var obj in menu.Items)
            if (obj is MenuItem mi)
                mi.IsChecked = ReferenceEquals(mi, selected);
    }

    private void CaptureHost_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || _tool == AnnotTool.None || MarkCanvas.IsHitTestVisible) return;
        var p = e.GetPosition(ShapeCanvas);
        if (_tool is AnnotTool.Text)
        {
            PlaceText(p);
            e.Handled = true;
            return;
        }
        if (_tool is AnnotTool.Step)
        {
            PlaceStep(p);
            e.Handled = true;
            return;
        }
        if (_tool is AnnotTool.Arrow or AnnotTool.Line)
        {
            e.Handled = true;
            if (_firstClick is null)
            {
                _firstClick = p;
                ClearPreview();
                CaptureHost.CaptureMouse();
                return;
            }

            if (_tool == AnnotTool.Line)
                AddLine(_firstClick.Value, p);
            else
                AddArrow(_firstClick.Value, p);
            _firstClick = null;
            ClearPreview();
            if (CaptureHost.IsMouseCaptured)
                CaptureHost.ReleaseMouseCapture();
            return;
        }
        if (_tool is AnnotTool.Rectangle or AnnotTool.Oval or AnnotTool.Blur)
        {
            _dragStart = p;
            ClearPreview();
            CaptureHost.CaptureMouse();
            e.Handled = true;
        }
    }

    private void CaptureHost_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragStart is not null)
            CompleteShapeDrag(Mouse.GetPosition(ShapeCanvas));
        else if (_firstClick is not null)
        {
            _firstClick = null;
            ClearPreview();
        }
    }

    private void CaptureHost_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_editing || MarkCanvas.IsHitTestVisible) return;
        var p = e.GetPosition(ShapeCanvas);
        if (_firstClick is { } a && _tool is AnnotTool.Arrow or AnnotTool.Line)
        {
            ClearPreview();
            var ln = new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = p.X, Y2 = p.Y,
                Stroke = Brush, StrokeThickness = _lineWidth,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.7
            };
            _previewShape = ln;
            ln.IsHitTestVisible = false;
            ShapeCanvas.Children.Add(ln);
            return;
        }

        if (_dragStart is not { } s) return;
        ClearPreview();
        var x = Math.Min(s.X, p.X);
        var y = Math.Min(s.Y, p.Y);
        var w = Math.Abs(s.X - p.X);
        var h = Math.Abs(s.Y - p.Y);
        if (_tool == AnnotTool.Rectangle)
            _previewShape = new Rectangle { Width = w, Height = h, Stroke = Brush, StrokeThickness = _lineWidth, Fill = Brushes.Transparent };
        else if (_tool == AnnotTool.Oval)
            _previewShape = new Ellipse { Width = w, Height = h, Stroke = Brush, StrokeThickness = _lineWidth, Fill = Brushes.Transparent };
        else if (_tool == AnnotTool.Blur)
            _previewShape = new Rectangle { Width = w, Height = h, Stroke = Brushes.DodgerBlue, StrokeThickness = 1, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
        if (_previewShape is not null)
        {
            Canvas.SetLeft(_previewShape, x);
            Canvas.SetTop(_previewShape, y);
            _previewShape.IsHitTestVisible = false;
            ShapeCanvas.Children.Add(_previewShape);
        }
    }

    private void CaptureHost_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || _dragStart is null || MarkCanvas.IsHitTestVisible) return;
        CompleteShapeDrag(e.GetPosition(ShapeCanvas));
        e.Handled = true;
    }

    private void CompleteShapeDrag(WpfPoint p)
    {
        if (_dragStart is not { } s) return;
        var x = Math.Min(s.X, p.X);
        var y = Math.Min(s.Y, p.Y);
        var w = Math.Abs(s.X - p.X);
        var h = Math.Abs(s.Y - p.Y);
        ClearPreview();
        _dragStart = null;
        if (CaptureHost.IsMouseCaptured)
            CaptureHost.ReleaseMouseCapture();
        if (w < 4 || h < 4) return;
        if (_tool == AnnotTool.Rectangle)
            AddShape(new Rectangle { Width = w, Height = h, Stroke = Brush, StrokeThickness = _lineWidth, Fill = Brushes.Transparent }, x, y);
        else if (_tool == AnnotTool.Oval)
            AddShape(new Ellipse { Width = w, Height = h, Stroke = Brush, StrokeThickness = _lineWidth, Fill = Brushes.Transparent }, x, y);
        else if (_tool == AnnotTool.Blur)
            AddBlur(x, y, w, h);
    }

    private Brush Brush => new SolidColorBrush(_lineColor);

    private void AddLine(WpfPoint a, WpfPoint b)
    {
        var ln = new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            Stroke = Brush, StrokeThickness = _lineWidth,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        ln.IsHitTestVisible = false;
        ShapeCanvas.Children.Add(ln);
        PushUndo(() => ShapeCanvas.Children.Remove(ln));
    }

    private void AddArrow(WpfPoint a, WpfPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len;
        var uy = dy / len;
        var head = Math.Max(10, _lineWidth * 4);
        var left = new WpfPoint(b.X - ux * head - uy * head * 0.45, b.Y - uy * head + ux * head * 0.45);
        var right = new WpfPoint(b.X - ux * head + uy * head * 0.45, b.Y - uy * head - ux * head * 0.45);

        var g = new Canvas();
        var shaft = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Brush, StrokeThickness = _lineWidth };
        var headPath = new Polygon
        {
            Points = new PointCollection { b, left, right },
            Fill = Brush, Stroke = Brush, StrokeThickness = 1
        };
        g.Children.Add(shaft);
        g.Children.Add(headPath);
        g.IsHitTestVisible = false;
        ShapeCanvas.Children.Add(g);
        PushUndo(() => ShapeCanvas.Children.Remove(g));
    }

    private void AddShape(Shape shape, double x, double y)
    {
        shape.IsHitTestVisible = false;
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        ShapeCanvas.Children.Add(shape);
        PushUndo(() => ShapeCanvas.Children.Remove(shape));
    }

    private void PlaceText(WpfPoint p)
    {
        var input = PromptText("Текст");
        if (string.IsNullOrWhiteSpace(input)) return;
        var tb = new TextBlock
        {
            Text = input,
            Foreground = Brush,
            FontSize = 14 + _lineWidth * 2,
            FontFamily = new FontFamily("Segoe UI"),
            Background = Brushes.Transparent
        };
        tb.IsHitTestVisible = false;
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y);
        ShapeCanvas.Children.Add(tb);
        PushUndo(() => ShapeCanvas.Children.Remove(tb));
    }

    private void PlaceStep(WpfPoint p)
    {
        _stepIndex++;
        const double d = 26;
        var ell = new Ellipse { Width = d, Height = d, Stroke = Brush, StrokeThickness = _lineWidth, Fill = new SolidColorBrush(Color.FromArgb(220, _lineColor.R, _lineColor.G, _lineColor.B)) };
        var txt = new TextBlock
        {
            Text = _stepIndex.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Width = d, Height = d,
            FontSize = 13
        };
        var host = new Grid { Width = d, Height = d };
        host.Children.Add(ell);
        host.Children.Add(txt);
        host.IsHitTestVisible = false;
        Canvas.SetLeft(host, p.X - d / 2);
        Canvas.SetTop(host, p.Y - d / 2);
        ShapeCanvas.Children.Add(host);
        PushUndo(() => ShapeCanvas.Children.Remove(host));
    }

    private void AddBlur(double x, double y, double w, double h)
    {
        if (_capturedBitmap is null) return;
        var composite = RenderForBlurCrop();
        var scaleX = composite.PixelWidth / _selW;
        var scaleY = composite.PixelHeight / _selH;
        var ix = (int)Math.Round(x * scaleX);
        var iy = (int)Math.Round(y * scaleY);
        var iw = (int)Math.Max(1, Math.Round(w * scaleX));
        var ih = (int)Math.Max(1, Math.Round(h * scaleY));
        if (ix + iw > composite.PixelWidth) iw = composite.PixelWidth - ix;
        if (iy + ih > composite.PixelHeight) ih = composite.PixelHeight - iy;
        if (iw <= 0 || ih <= 0) return;

        var crop = new CroppedBitmap(composite, new Int32Rect(ix, iy, iw, ih));
        crop.Freeze();
        var baked = BakeBlurredPatch(crop, w, h, CaptureHost);
        var img = new Image
        {
            Source = baked,
            Width = w,
            Height = h,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);

        BlurCanvas.Children.Add(img);
        RefreshInkLayerAfterBlur();

        PushUndo(() =>
        {
            BlurCanvas.Children.Remove(img);
            RefreshInkLayerAfterBlur();
        });
    }

    private void PushUndo(Action undo) => _undo.Push(undo);

    private void ClearPreview()
    {
        if (_previewShape is null) return;
        ShapeCanvas.Children.Remove(_previewShape);
        _previewShape = null;
    }

    private void UndoInk_OnClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count > 0) { _undo.Pop()(); return; }
        if (MarkCanvas.Strokes.Count > 0)
        {
            MarkCanvas.Strokes.RemoveAt(MarkCanvas.Strokes.Count - 1);
            UpdateMarkCanvasVisibility();
        }
    }

    private string? PromptText(string defaultText)
    {
        var dlg = new Window
        {
            Title = "Текст",
            Width = 380,
            Height = 140,
            Owner = this,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };
        var tb = new TextBox { Text = defaultText, Margin = new Thickness(12, 12, 12, 0), Height = 28 };
        var cancel = new Button { Content = "Отмена", Width = 88, Margin = new Thickness(0, 8, 8, 12), IsCancel = true };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 8, 12, 12), IsDefault = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(tb);
        dlg.Content = panel;
        dlg.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Escape)
            {
                dlg.DialogResult = false;
                dlg.Close();
            }
        };
        tb.Focus();
        tb.SelectAll();
        return dlg.ShowDialog() == true ? tb.Text : null;
    }
}
