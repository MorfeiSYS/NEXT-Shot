using System.Windows.Controls;
using System.Windows.Media;

namespace NEXTShot.Helpers;

public static class BrushContrast
{
    public static void ApplyButtonForeground(Button button)
    {
        if (button.Background is not SolidColorBrush brush)
            return;
        button.Foreground = PickForeground(brush.Color);
    }

    public static Brush PickForeground(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return new SolidColorBrush(luminance > 0.55 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White);
    }
}