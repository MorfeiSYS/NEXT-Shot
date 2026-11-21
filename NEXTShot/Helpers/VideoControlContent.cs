using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NEXTShot.Helpers;

public static class VideoControlContent
{
    public static void ApplyStartButton(Button btn, bool useIcons, bool sessionActive, bool paused)
    {
        if (!sessionActive)
        {
            btn.ToolTip = "Старт";
            btn.Content = useIcons ? BuildIcon("\u25B6", "#FF2EA3FF") : "Старт";
            btn.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xA3, 0xFF));
        }
        else if (paused)
        {
            btn.ToolTip = "Продолжить";
            btn.Content = useIcons ? BuildPauseIcon() : "Пауза";
            btn.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x22, 0x22));
        }
        else
        {
            btn.ToolTip = "Пауза";
            btn.Content = useIcons ? BuildPauseIcon() : "Пауза";
            btn.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0xA3, 0xFF));
        }

        BrushContrast.ApplyButtonForeground(btn);
    }

    public static void ApplyStopButton(Button btn, bool useIcons, bool enabled)
    {
        btn.IsEnabled = enabled;
        btn.ToolTip = "Стоп";
        btn.Content = useIcons ? BuildStopIcon() : "Стоп";
        btn.Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
        BrushContrast.ApplyButtonForeground(btn);
    }

    public static void ApplyOverlayRecord(Button btn, bool useIcons, bool sessionActive)
    {
        btn.ToolTip = sessionActive ? "Остановить запись" : "Запись видео";
        if (sessionActive)
        {
            btn.Content = useIcons ? BuildToolbarStopShape() : "Стоп";
            ApplyOverlayToolbarTextStyle(btn, !useIcons);
        }
        else
        {
            btn.Content = useIcons ? BuildRecordDotIcon() : BuildRecordDotContent();
            ResetOverlayToolbarButtonStyle(btn, useIcons);
        }
    }

    public static void ApplyOverlayPause(Button btn, bool useIcons, bool paused)
    {
        btn.ToolTip = paused ? "Продолжить" : "Пауза";
        if (useIcons)
        {
            btn.Content = BuildToolbarPauseShape();
            ResetOverlayToolbarButtonStyle(btn, true);
        }
        else
        {
            btn.Content = "Пауза";
            ApplyOverlayToolbarTextStyle(btn, true);
        }

        if (paused)
            btn.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x22, 0x22));
        else
            btn.ClearValue(Control.BackgroundProperty);
    }

    private static void ApplyOverlayToolbarTextStyle(Button btn, bool wide)
    {
        btn.SetResourceReference(Control.FontFamilyProperty, "Skr.Font");
        btn.FontSize = 11;
        btn.Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        btn.Padding = new Thickness(4, 2, 4, 2);
        btn.Width = wide ? double.NaN : 38;
        btn.MinWidth = wide ? 40 : 38;
    }

    private static void ResetOverlayToolbarButtonStyle(Button btn, bool iconMode)
    {
        btn.ClearValue(Control.FontFamilyProperty);
        btn.ClearValue(Control.FontSizeProperty);
        btn.ClearValue(Control.ForegroundProperty);
        btn.Padding = new Thickness(2);
        btn.Width = 38;
        btn.MinWidth = 38;
    }

    public static string FormatDuration(TimeSpan d)
    {
        if (d.TotalHours >= 1)
            return d.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        return d.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static UIElement BuildIcon(string glyph, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        return new TextBlock
        {
            Text = glyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(color == Color.FromRgb(0x2E, 0xA3, 0xFF) ? Colors.White : color),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static UIElement BuildPauseIcon() => new TextBlock
    {
        Text = "\u23F8",
        FontSize = 16,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement BuildToolbarStopShape() => new Border
    {
        Width = 14,
        Height = 14,
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
        CornerRadius = new CornerRadius(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement BuildToolbarPauseShape()
    {
        var fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
        var grid = new Grid { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 4, Height = 14, Fill = fill, HorizontalAlignment = HorizontalAlignment.Left
        });
        grid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 4, Height = 14, Fill = fill, HorizontalAlignment = HorizontalAlignment.Right
        });
        return grid;
    }

    private static UIElement BuildToolbarPlayShape() => new System.Windows.Shapes.Polygon
    {
        Points = new PointCollection { new(0, 0), new(14, 7), new(0, 14) },
        Fill = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement BuildStopIcon() => new Border
    {
        Width = 14,
        Height = 14,
        Background = Brushes.White,
        CornerRadius = new CornerRadius(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement BuildRecordDotIcon() => new Border
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x22, 0x22)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static UIElement BuildRecordDotContent() => new Border
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
    };
}