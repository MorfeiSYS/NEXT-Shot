using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NEXTShot.Helpers;

public static class PauseButtonBlink
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMilliseconds(2600);

    public static Storyboard Begin(Button button)
    {
        button.Opacity = 1;
        button.Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x22, 0x22));

        var colorAnim = new ColorAnimation(
            Color.FromRgb(0xEE, 0x62, 0x52),
            Color.FromRgb(0x96, 0x24, 0x24),
            Cycle)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var opacityAnim = new DoubleAnimation(1.0, 0.82, Cycle)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(colorAnim, button);
        Storyboard.SetTargetProperty(colorAnim, new PropertyPath("(Button.Background).(SolidColorBrush.Color)"));
        Storyboard.SetTarget(opacityAnim, button);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(colorAnim);
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
        return storyboard;
    }

    public static void Stop(Storyboard? storyboard, Button button, bool clearBackground)
    {
        storyboard?.Stop();
        button.BeginAnimation(UIElement.OpacityProperty, null);
        button.Opacity = 1;
        if (clearBackground)
            button.ClearValue(Control.BackgroundProperty);
    }
}