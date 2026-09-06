using System.Windows;
using System.Windows.Media;

namespace Petal.Windows;

internal static class WindowsTheme
{
    public static Brush Foreground => System.Windows.Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush ?? SystemColors.ControlTextBrush;
    public static Brush Secondary => System.Windows.Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? SystemColors.GrayTextBrush;
}
