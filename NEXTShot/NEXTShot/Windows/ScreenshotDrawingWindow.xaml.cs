using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NEXTShot.Services;

namespace NEXTShot.Windows;

public partial class ScreenshotDrawingWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _path;
    private readonly Bitmap _bitmap;

    public ScreenshotDrawingWindow(Bitmap bitmap, AppSettings settings, string path)
    {
        _bitmap = bitmap;
        _settings = settings;
        _path = path;
        InitializeComponent();
        var source = ScreenCaptureService.ToBitmapSource(bitmap);
        source.Freeze();
        BackgroundImage.Source = source;
        DrawingCanvas.Width = bitmap.Width;
        DrawingCanvas.Height = bitmap.Height;
        Width = Math.Min(1280, bitmap.Width + 120);
        Height = Math.Min(920, bitmap.Height + 80);
    }

    private void ToolPen_OnClick(object sender, RoutedEventArgs e) => DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
    private void ToolInk_OnClick(object sender, RoutedEventArgs e) => DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;

    private void Undo_OnClick(object sender, RoutedEventArgs e)
    {
        if (DrawingCanvas.Strokes.Count > 0) DrawingCanvas.Strokes.RemoveAt(DrawingCanvas.Strokes.Count - 1);
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        using var merged = Merge();
        ScreenCaptureService.SaveBitmap(merged, _path, _settings.JpegQuality, _settings.ScreenshotFileType == 1);
        MessageBox.Show($"Сохранено:\n{_path}", "Скриншотер");
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        using var merged = Merge();
        Clipboard.SetImage(ScreenCaptureService.ToBitmapSource(merged));
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Undo_OnClick(sender, e);
    }

    private Bitmap Merge()
    {
        var rtb = new RenderTargetBitmap(_bitmap.Width, _bitmap.Height, 96, 96, PixelFormats.Pbgra32);
        DrawingCanvas.Measure(new System.Windows.Size(_bitmap.Width, _bitmap.Height));
        DrawingCanvas.Arrange(new Rect(0, 0, _bitmap.Width, _bitmap.Height));
        rtb.Render(DrawingCanvas);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var overlay = new Bitmap(ms);
        var result = new Bitmap(_bitmap.Width, _bitmap.Height);
        using var g = Graphics.FromImage(result);
        g.DrawImage(_bitmap, 0, 0);
        g.DrawImage(overlay, 0, 0);
        return result;
    }
}