using System.Drawing;
using System.IO;
using System.Windows;
using NEXTShot.Helpers;
using NEXTShot.Services;

namespace NEXTShot.Windows;

public partial class ScreenshotActionWindow : Window
{
    private readonly Bitmap _bitmap;
    private readonly AppSettings _settings;
    private readonly string _path;

    public ScreenshotActionWindow(Bitmap bitmap, AppSettings settings, string path)
    {
        _bitmap = bitmap;
        _settings = settings;
        _path = path;
        InitializeComponent();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        ScreenCaptureService.SaveBitmap(_bitmap, _path, _settings.JpegQuality, _settings.ScreenshotFileType == 1);
        MessageBox.Show($"Файл успешно сохранен на диск.\n{_path}", NEXTShot.Resources.SkrinshoterStrings.AppName);
        Close();
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ClipboardHelper.TrySetImage(ScreenCaptureService.ToBitmapSource(_bitmap)))
        {
            MessageBox.Show(ClipboardHelper.ClipboardBusyMessage, NEXTShot.Resources.SkrinshoterStrings.AppName);
            return;
        }

        Close();
    }

    private void Cloud_OnClick(object sender, RoutedEventArgs e)
    {
        ScreenCaptureService.SaveBitmap(_bitmap, _path, _settings.JpegQuality, _settings.ScreenshotFileType == 1);
        if (!ClipboardHelper.TrySetText(_path))
        {
            MessageBox.Show(ClipboardHelper.ClipboardBusyMessage, NEXTShot.Resources.SkrinshoterStrings.AppName);
            return;
        }

        MessageBox.Show($"Облако отключено. Файл сохранен локально, путь скопирован в буфер.\n{_path}", NEXTShot.Resources.SkrinshoterStrings.AppName);
        Close();
    }

    private void Edit_OnClick(object sender, RoutedEventArgs e)
    {
        var clone = (Bitmap)_bitmap.Clone();
        Hide();
        new ScreenshotDrawingWindow(clone, _settings, _path).ShowDialog();
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}