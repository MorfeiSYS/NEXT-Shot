using System.Windows;

namespace NEXTShot.Controls;

public static class CaptureToolIcon
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected",
        typeof(bool),
        typeof(CaptureToolIcon),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CheckedSourceProperty = DependencyProperty.RegisterAttached(
        "CheckedSource",
        typeof(string),
        typeof(CaptureToolIcon),
        new PropertyMetadata(null));

    public static void SetIsSelected(DependencyObject element, bool value) =>
        element.SetValue(IsSelectedProperty, value);

    public static bool GetIsSelected(DependencyObject element) =>
        (bool)element.GetValue(IsSelectedProperty);

    public static void SetCheckedSource(DependencyObject element, string? value) =>
        element.SetValue(CheckedSourceProperty, value);

    public static string? GetCheckedSource(DependencyObject element) =>
        (string?)element.GetValue(CheckedSourceProperty);
}
