using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Petal.Core;

namespace Petal.Windows;

internal sealed class ShortcutDialog : Window
{
    internal string? CapturedBinding { get; private set; }
    public ShortcutDialog(AppController app)
    {
        Title = "Recording shortcut"; Width = 460; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "Press your shortcut", FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Use a single key, such as F5 or Home, or hold modifiers with another key. Escape cancels.", Margin = new Thickness(0, 12, 0, 16) });
        var captured = new TextBlock { Text = "Waiting for a key…", FontSize = 18, Margin = new Thickness(0, 8, 0, 16) }; body.Children.Add(captured);
        var note = new TextBlock { Text = "This key will be reserved for Petal while it is running. Fn keys controlled by keyboard firmware cannot be captured.", FontSize = 12 }; body.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save", IsEnabled = false };
        actions.Children.Add(cancel); actions.Children.Add(save); body.Children.Add(actions); Content = body;
        void CaptureKey(uint key, uint modifiers)
        {
            try { CapturedBinding = ShortcutBinding.FromKey(key, modifiers).Label; captured.Text = CapturedBinding; save.IsEnabled = true; }
            catch (ArgumentException ex) { note.Text = ex.Message; CapturedBinding = null; save.IsEnabled = false; }
        }
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle).AddHook(
            (IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled) =>
            {
                if (message == 0x0319)
                {
                    uint command = ((uint)lparam.ToInt64() >> 16) & 0xFFF;
                    uint key = command switch { 8 => 0xADu, 9 => 0xAEu, 10 => 0xAFu, 11 => 0xB0u, 12 => 0xB1u, 13 => 0xB2u, 14 => 0xB3u, _ => 0u };
                    if (key != 0) { CaptureKey(key, 0); handled = true; return new IntPtr(1); }
                }
                return IntPtr.Zero;
            });
        PreviewKeyDown += (_, e) =>
        {
            e.Handled = true; if (e.IsRepeat) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Close(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            uint modifiers = 0; var state = Keyboard.Modifiers;
            if (state.HasFlag(ModifierKeys.Control)) modifiers |= 2;
            if (state.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
            if (state.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
            if (state.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
            CaptureKey((uint)KeyInterop.VirtualKeyFromKey(key), modifiers);
        };
        save.Click += (_, _) => { if (CapturedBinding != null && app.TrySetShortcut(CapturedBinding)) Close(); else note.Text = app.Status; };
    }
}
