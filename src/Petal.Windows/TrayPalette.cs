using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace Petal.Windows;

internal sealed class TrayPalette : Window
{
    readonly AppController app;
    readonly Action appChanged;
    IntPtr target;
    internal IntPtr RecordingTarget => target;
    readonly StackPanel results = new();
    readonly Button record = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    public TrayPalette(AppController app)
    {
        this.app = app;
        Title = "Petal palette"; Width = 340; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1) });
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Petal", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        record.Click += (_, _) => { Hide(); _ = app.ToggleRecording(target); };
        root.Children.Add(record);
        root.Children.Add(new TextBlock { Text = "Recent transcripts", FontSize = 12, Margin = new Thickness(0, 18, 0, 10), Opacity = .7 });
        root.Children.Add(results);
        Content = new Border { Padding = new Thickness(16), Child = root };
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SourceInitialized += (_, _) => Native.Acrylic(this);
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        appChanged = () => { if (IsVisible) { Refresh(); PositionNearTray(); } };
        app.Changed += appChanged;
        IsVisibleChanged += (_, _) => { if (!IsVisible) results.Children.Clear(); };
        Closed += (_, _) => app.Changed -= appChanged;
    }

    void Refresh()
    {
        record.Content = (app.Phase == "recording" ? "Finish recording" : "Start recording") + "   ·   " + app.Storage.Preferences.Shortcut;
        record.IsEnabled = !app.Busy || app.Phase == "recording";
        results.Children.Clear();
        var entries = app.Storage.History.OrderByDescending(x => x.Timestamp).Take(4).ToList();
        if (entries.Count == 0)
            results.Children.Add(new TextBlock { Text = "No recordings yet", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 28), Opacity = .7 });
        foreach (var entry in entries)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = entry.Text.ReplaceLineEndings(" "), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
            var caption = new TextBlock { Text = entry.Timestamp.ToLocalTime().ToString("MMM d · h:mm tt"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), Opacity = .65 };
            content.Children.Add(caption);
            var button = new Button { Content = content, ToolTip = "Copy transcript", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 8, 10, 8) };
            System.Windows.Automation.AutomationProperties.SetName(button, "Copy transcript: " + entry.Text);
            button.Click += (_, _) => { app.Copy(entry.Text); Hide(); };
            results.Children.Add(button);
        }
    }

    void PositionNearTray()
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    public void Open()
    {
        target = Native.GetForegroundWindow(); Refresh();
        Show(); PositionNearTray(); Activate(); record.Focus();
    }

    internal void Capture(string directory)
    {
        Refresh(); Show(); UpdateLayout();
        if (results.Children.OfType<Button>().Count() != Math.Min(4, app.Storage.History.Count))
            throw new InvalidOperationException("Palette must show at most four recent transcripts.");
        var root = (FrameworkElement)Content;
        SettingsWindow.Capture(root, System.IO.Path.Combine(directory, "tray-palette.png"), (int)root.ActualWidth, (int)root.ActualHeight);
        Hide();
    }
}
