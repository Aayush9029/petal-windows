using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Petal.Core;

namespace Petal.Windows;

internal static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    public static bool Acrylic(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || SystemParameters.HighContrast) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        int corners = 2, backdrop = 3;
        var color = (window.TryFindResource("SolidBackgroundFillColorBaseBrush") as System.Windows.Media.SolidColorBrush)?.Color;
        int dark = color.HasValue && color.Value.R < 128 ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        if (DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) != 0) return false;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0) return false;
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        window.Background = System.Windows.Media.Brushes.Transparent;
        return true;
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT keyboard; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    public static void NoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLongPtr(hwnd, -20, (IntPtr)(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000 | 0x80));
    }
    public static bool Paste()
    {
        INPUT Key(ushort key, bool up) => new() { type = 1, data = new() { keyboard = new() { vk = key, flags = up ? 2u : 0u } } };
        INPUT[] keys = [Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true)];
        return SendInput(4, keys, Marshal.SizeOf<INPUT>()) == 4;
    }
}

internal sealed class Shortcut : IDisposable
{
    readonly HwndSource source;
    readonly Action pressed;
    public Shortcut(Action pressed, string binding)
    {
        this.pressed = pressed;
        var keys = ShortcutBinding.Get(binding);
        source = new HwndSource(new HwndSourceParameters("Petal shortcut") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
        if (!Native.RegisterHotKey(source.Handle, 1, 0x4000 | keys.Modifiers, keys.Key))
        { source.Dispose(); throw new InvalidOperationException("That shortcut is reserved or used by another app. Choose another key."); }
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312) { pressed(); handled = true; }
        return IntPtr.Zero;
    }
    public void Dispose() { Native.UnregisterHotKey(source.Handle, 1); source.Dispose(); }
}
